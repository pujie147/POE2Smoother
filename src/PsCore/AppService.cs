using System.Net.Http;
using LibBundle3.Records;
using PsRules;

namespace PsCore;

public sealed record PatchProgress(int Done, int Total, string Path, string Phase);

public sealed class ApplyRequest
{
    public required GameLocation Game { get; init; }
    public required RulePack Pack { get; init; }
    public string RulePackPath { get; init; } = "";
    public string Preset { get; init; } = "safe";
    public IgnoreList Ignore { get; init; } = IgnoreList.Default();
    public bool IncludeExperimental { get; init; }
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlySet<string>? CategoryFilter { get; init; }
}

public sealed class ApplyResult
{
    public required PatchPlan Plan { get; init; }
    public string BackupPath { get; init; } = "";
    public string OriginalSha256 { get; init; } = "";
    public string AppliedSha256 { get; init; } = "";
    public bool DryRun { get; init; }
}

public sealed class RestoreRequest
{
    public required GameLocation Game { get; init; }
    public string? BackupPath { get; init; }
    public string? IndexUrl { get; init; }
    public bool Force { get; init; }
    public bool CleanOnly { get; init; }
}

public sealed class RestoreResult
{
    public bool RestoredIndex { get; init; }
    public bool CleanedCustomBundles { get; init; }
    public string Message { get; init; } = "";

    // Per-file restore details (only meaningful when PerFile = true).
    public bool PerFile { get; init; }
    public int FilesRestored { get; init; }
    public int FilesSkipped { get; init; }
    public int FilesUnresolved { get; init; }
    public IReadOnlyList<string> SkippedPaths { get; init; } = [];
}

public sealed class StatusResult
{
    public required GameLocation Game { get; init; }
    public string CurrentIndexSha256 { get; init; } = "";
    public AppState? State { get; init; }
    public bool IsModified { get; init; }
    public bool GameUpdated { get; init; }
    public bool CustomBundlesExist { get; init; }
    public bool BackupExists { get; init; }
}

public sealed class AppService
{
    private readonly AppPaths _paths;

    public AppService(AppPaths paths)
    {
        _paths = paths;
        _paths.Ensure();
    }

    public AppPaths Paths => _paths;

    public PatchPlan BuildPlan(ApplyRequest request)
    {
        using var session = IndexSession.OpenReadOnly(request.Game);
        return BuildPlan(session, request);
    }

    public ApplyResult Apply(ApplyRequest request, IProgress<PatchProgress>? progress = null)
    {
        var store = TargetStore.For(request.Game);

        if (!store.Exists())
            throw new FileNotFoundException("PoE2 data file not found", store.TargetPath);

        if (GameProcessGuard.IsGameRunning(out var processes) && !request.Force)
            throw new InvalidOperationException(
                $"PoE2 appears to be running ({string.Join(", ", processes)}). Close the game first.");

        _paths.Ensure();
        var state = LoadMatchingState(request.Game);
        var currentHash = store.HashIndex();

        // Return to a known baseline before rebuilding the patch.
        if (state != null)
        {
            if (currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(state.BackupPath))
                    throw new FileNotFoundException("Backup of the applied state is missing", state.BackupPath);

                store.RestoreBackup(state.BackupPath);
                store.DeleteCustomBundles();
                currentHash = store.HashIndex();
                StateStore.Delete(_paths);
                state = null;
                AppLog.Info(_paths, "Restored original index from state before applying a new plan.");
            }
            else if (!currentHash.Equals(state.OriginalIndexSha256, StringComparison.OrdinalIgnoreCase))
            {
                // The game was updated (or the index was changed by another tool).
                if (store.HasCustomBundles() && !request.Force)
                    throw new InvalidOperationException(
                        "Index changed since the last apply (game update?) and custom bundles still exist. " +
                        "Use --force to discard the old state and continue.");

                StateStore.Delete(_paths);
                store.DeleteCustomBundles();
                state = null;
                AppLog.Warn(_paths, "Index changed since last apply; old state discarded.");
            }
            else
            {
                store.DeleteCustomBundles();
                StateStore.Delete(_paths);
                state = null;
            }
        }
        else if (store.HasCustomBundles())
        {
            if (!request.Force)
                throw new InvalidOperationException(
                    "LibGGPK3 custom bundles exist but there is no state file. " +
                    "Restore/clean them first, or use --force to discard them.");
            store.DeleteCustomBundles();
        }

        var plan = BuildPlan(request);
        if (request.DryRun || plan.Changes.Count == 0)
        {
            return new ApplyResult
            {
                Plan = plan,
                OriginalSha256 = currentHash,
                AppliedSha256 = currentHash,
                DryRun = request.DryRun
            };
        }

        string backupPath;
        try
        {
            store.EnsureWritable(plan.TotalBytes, request.Force);
            backupPath = store.CreateBackup(_paths.BackupsDir);
        }
        catch
        {
            // Never leave the standalone write marker behind on a pre-flight failure: the next
            // apply would then refuse to run until --force.
            store.ClearWriteMarker();
            throw;
        }

        AppLog.Info(_paths, $"Backup created: {backupPath}");

        var committed = false;
        var patchedFiles = new List<PatchedFileEntry>();
        try
        {
            using (var work = IndexSession.OpenWorkCopy(request.Game))
            {
                var total = plan.Changes.Count;
                var reportStep = Math.Max(1, total / 100);
                var done = 0;

                foreach (var change in plan.Changes)
                {
                    if (!work.TryGetFile(change.Path, out var record) || record == null)
                        throw new FileNotFoundException("File disappeared from index", change.Path);

                    var replacement = change.Strategy is StrategyKind.StripAttachments or StrategyKind.Auto
                        ? ReplacementFactory.Create(record.Read().ToArray(), change.Path, change.Strategy)
                        : ReplacementFactory.Create([], change.Path, change.Strategy);

                    // Capture the original coordinates before Write() rewrites the record,
                    // so a later per-file restore can redirect back into the official bundle.
                    var entry = new PatchedFileEntry
                    {
                        Path = change.Path,
                        Strategy = change.Strategy.ToString(),
                        OriginalBundlePath = record.BundleRecord?.Path ?? "",
                        OriginalOffset = record.Offset,
                        OriginalSize = record.Size,
                        AppliedSize = replacement.LongLength
                    };

                    record.Write(replacement);

                    entry.AppliedSha256 = HashUtil.Sha256Bytes(replacement);
                    entry.AppliedBundlePath = record.BundleRecord?.Path ?? "";
                    entry.AppliedOffset = record.Offset;
                    patchedFiles.Add(entry);

                    done++;
                    if (done == total || done % reportStep == 0)
                        progress?.Report(new PatchProgress(done, total, change.Path, "write"));
                }

                progress?.Report(new PatchProgress(done, total, "", "save"));
                work.Save();
                work.Commit();
                committed = true;
            }

            var appliedHash = store.HashIndex();
            var newState = new AppState
            {
                GameRoot = request.Game.GameRoot,
                IndexPath = request.Game.IndexPath,
                TargetPath = store.TargetPath,
                TargetKind = request.Game.Kind.ToString(),
                OriginalIndexSha256 = currentHash,
                AppliedIndexSha256 = appliedHash,
                BackupPath = backupPath,
                RulePackName = request.Pack.Name,
                RulePackPath = request.RulePackPath,
                Preset = request.Preset,
                FileCount = plan.Changes.Count,
                CustomBundleFiles = store.ListCustomBundles().ToList(),
                PatchedFiles = patchedFiles
            };
            StateStore.Save(_paths, newState);
            store.ClearWriteMarker();
            BackupManager.Prune(_paths.BackupsDir, 10);
            AppLog.Info(_paths,
                $"Applied {plan.Changes.Count} changes (preset={request.Preset}, pack={request.Pack.Name}).");

            return new ApplyResult
            {
                Plan = plan,
                BackupPath = backupPath,
                OriginalSha256 = currentHash,
                AppliedSha256 = appliedHash
            };
        }
        catch
        {
            try
            {
                store.DeleteCustomBundles();
            }
            catch (Exception cleanupEx)
            {
                // A locked or damaged container must not hide the real failure.
                AppLog.Warn(_paths, $"Apply cleanup failed: {cleanupEx.Message}");
            }

            if (committed)
            {
                try
                {
                    store.RestoreBackup(backupPath);
                    AppLog.Warn(_paths, "Apply failed after commit; backup restored.");
                }
                catch (Exception restoreEx)
                {
                    AppLog.Error(_paths, $"Apply rollback failed: {restoreEx}");
                }
            }

            store.ClearWriteMarker();
            throw;
        }
    }

    public RestoreResult Restore(RestoreRequest request)
    {
        if (GameProcessGuard.IsGameRunning(out var processes) && !request.Force)
            throw new InvalidOperationException(
                $"PoE2 appears to be running ({string.Join(", ", processes)}). Close the game first.");

        var store = TargetStore.For(request.Game);
        var state = LoadMatchingState(request.Game);
        var currentHash = store.Exists() ? store.HashIndex() : "";

        string? backup = request.BackupPath ?? state?.BackupPath;
        if (!string.IsNullOrWhiteSpace(request.IndexUrl))
        {
            if (request.Game.IsGgpk)
                throw new InvalidOperationException(
                    "--index-url downloads a Bundles2/_.index.bin and only works for the Steam client.");

            backup = DownloadIndex(request.IndexUrl, request.Game.BundlesDir);
        }

        var cleaned = false;

        if (request.CleanOnly)
        {
            if (store.HasCustomBundles())
            {
                store.DeleteCustomBundles();
                cleaned = true;
            }
            StateStore.Delete(_paths);
            return new RestoreResult
            {
                CleanedCustomBundles = cleaned,
                Message = "Custom bundles removed (index unchanged)."
            };
        }

        // Safety-first per-file restore: when the state carries per-file fingerprints (schema v2),
        // the index drifted from what this tool produced (game update?), and the user did not ask
        // for a whole-index overwrite, restore only files whose live content still matches exactly
        // what we wrote. Files the game changed are skipped, so an update is never downgraded.
        if (state is { PatchedFiles.Count: > 0 } &&
            !request.Force &&
            string.IsNullOrWhiteSpace(request.BackupPath) &&
            string.IsNullOrWhiteSpace(request.IndexUrl) &&
            !currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase) &&
            store.Exists())
        {
            return RestorePerFile(state, request.Game, store);
        }

        if (string.IsNullOrWhiteSpace(backup))
            throw new InvalidOperationException("No backup path is known. Run with --backup <file> or apply first.");

        if (!File.Exists(backup))
            throw new FileNotFoundException("Backup not found", backup);

        var backupHash = HashUtil.Sha256File(backup);
        if (state != null && !currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Force)
                throw new InvalidOperationException(
                    "The current index is not the one produced by this tool (game updated or changed). " +
                    "Refusing to overwrite it with an old backup. Use --force if you are sure.");

            AppLog.Warn(_paths, "Restore forced over a changed index.");
        }

        if (currentHash.Equals(backupHash, StringComparison.OrdinalIgnoreCase))
        {
            // Custom bundles are already orphans once the index matches the backup bytes.
            if (store.HasCustomBundles())
            {
                store.DeleteCustomBundles();
                cleaned = true;
            }

            StateStore.Delete(_paths);
            return new RestoreResult
            {
                RestoredIndex = false,
                CleanedCustomBundles = cleaned,
                Message = "Index already matches the backup."
            };
        }

        // Index first, bundles second: if the write fails halfway, the live index still points at
        // bundles that exist. Doing it the other way round would leave dangling references.
        store.RestoreBackup(backup);

        if (store.HasCustomBundles())
        {
            store.DeleteCustomBundles();
            cleaned = true;
        }

        StateStore.Delete(_paths);
        AppLog.Info(_paths, $"Restored index from backup: {backup}");

        return new RestoreResult
        {
            RestoredIndex = true,
            CleanedCustomBundles = cleaned,
            Message = $"Restored {store.TargetPath}"
        };
    }

    /// <summary>
    /// Restores every file this tool suppressed, but only those still consistent with the bytes
    /// it wrote at apply time: their record is redirected back to the original coordinates in the
    /// official bundle (no data is rewritten, so the custom bundle files become orphans and are
    /// dropped afterwards). Files whose content no longer matches (game update, external edit)
    /// are skipped and keep their current content — that is what prevents post-update downgrades.
    /// </summary>
    private RestoreResult RestorePerFile(AppState state, GameLocation game, TargetStore store)
    {
        var restored = 0;
        var skipped = new List<string>();
        var unresolved = new List<PatchedFileEntry>();

        using (var work = IndexSession.OpenWorkCopy(game))
        {
            // Redirect targets reference official bundles by path; an update may have renamed or
            // removed them, so resolve against the live index's bundle table.
            var bundlesByPath = new Dictionary<string, BundleRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in work.Index.Bundles.Span)
            {
                var bundlePath = bundle.Path;
                if (!string.IsNullOrEmpty(bundlePath))
                    bundlesByPath[bundlePath] = bundle;
            }

            foreach (var entry in state.PatchedFiles)
            {
                if (!work.TryGetFile(entry.Path, out var record) || record == null)
                {
                    skipped.Add(entry.Path); // removed/renamed by the update
                    continue;
                }

                bool matches;
                try
                {
                    matches = record.Size == entry.AppliedSize &&
                              HashUtil.Sha256Bytes(record.Read().ToArray())
                                  .Equals(entry.AppliedSha256, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    // Cannot read the live content (its custom bundle is missing or corrupt): the
                    // file is broken either way, so treat it as ours and redirect if we can.
                    AppLog.Warn(_paths, $"Per-file restore: cannot read {entry.Path}: {ex.Message}");
                    matches = true;
                }

                if (!matches)
                {
                    skipped.Add(entry.Path); // changed since we wrote it: never overwrite with old bytes
                    continue;
                }

                if (string.IsNullOrEmpty(entry.OriginalBundlePath) ||
                    !bundlesByPath.TryGetValue(entry.OriginalBundlePath, out var originalBundle))
                {
                    unresolved.Add(entry); // still ours, but the original location is gone: keep suppression
                    continue;
                }

                record.Redirect(originalBundle, (int)entry.OriginalOffset, (int)entry.OriginalSize);
                restored++;
            }

            if (restored > 0)
            {
                work.Save();
                work.Commit();
            }
        }

        // Drop only the custom bundles no live record references anymore; bundles still backing a
        // kept-suppressed file or an edit made by another tool stay on disk.
        // Inside a GGPK the index Save above already deleted every emptied custom bundle, so there
        // is nothing to sweep there.
        var cleanedBundles = false;
        if (!game.IsGgpk && store.HasCustomBundles())
        {
            var deleted = RemoveOrphanCustomBundles(game);
            cleanedBundles = deleted > 0;
            AppLog.Info(_paths, $"Per-file restore: removed {deleted} orphan custom bundle(s).");
        }

        if (unresolved.Count == 0)
        {
            StateStore.Delete(_paths);
            AppLog.Info(_paths,
                $"Per-file restore: restored={restored}, skipped={skipped.Count} (kept current content).");
        }
        else
        {
            // Keep the old applied/original hashes so a later restore stays on the safe per-file
            // path; only the still-suppressed entries remain tracked.
            StateStore.Save(_paths, new AppState
            {
                SchemaVersion = state.SchemaVersion,
                GameRoot = state.GameRoot,
                IndexPath = state.IndexPath,
                TargetPath = state.TargetPath,
                TargetKind = state.TargetKind,
                OriginalIndexSha256 = state.OriginalIndexSha256,
                AppliedIndexSha256 = state.AppliedIndexSha256,
                BackupPath = state.BackupPath,
                RulePackName = state.RulePackName,
                RulePackPath = state.RulePackPath,
                Preset = state.Preset,
                AppliedAt = state.AppliedAt,
                FileCount = unresolved.Count,
                CustomBundleFiles = store.ListCustomBundles().ToList(),
                PatchedFiles = unresolved
            });
            AppLog.Warn(_paths,
                $"Per-file restore: restored={restored}, skipped={skipped.Count}, unresolved={unresolved.Count} " +
                "(original bundle missing; their suppression is kept). Verify game files, then restore again.");
        }

        var message = unresolved.Count > 0
            ? $"Per-file restore: {restored} restored, {skipped.Count} skipped, {unresolved.Count} kept suppressed (original bundle missing)."
            : $"Per-file restore: {restored} restored, {skipped.Count} skipped (changed by update, kept new content).";
        if (skipped.Count > 0)
        {
            message += " Skipped: " + string.Join(", ", skipped.Take(10)) +
                       (skipped.Count > 10 ? $" (+{skipped.Count - 10} more)" : "");
        }

        return new RestoreResult
        {
            RestoredIndex = restored > 0,
            CleanedCustomBundles = cleanedBundles,
            PerFile = true,
            FilesRestored = restored,
            FilesSkipped = skipped.Count,
            FilesUnresolved = unresolved.Count,
            SkippedPaths = skipped,
            Message = message
        };
    }

    private int RemoveOrphanCustomBundles(GameLocation game)
    {
        var referenced = new List<string?>();
        using (var read = IndexSession.OpenReadOnly(game))
        {
            foreach (var file in read.Index.Files.Values)
                referenced.Add(file.BundleRecord?.Path);
        }

        return BackupManager.DeleteOrphanCustomBundles(game.BundlesDir, referenced);
    }

    private string DownloadIndex(string url, string bundlesDir)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Index URL must be an absolute http/https URL.", nameof(url));

        _paths.Ensure();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var bytes = http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
        if (bytes.Length < 1024)
            throw new InvalidDataException($"Downloaded index is too small: {bytes.Length} bytes");

        var path = Path.Combine(_paths.BackupsDir,
            $"download-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-_.index.bin");
        File.WriteAllBytes(path, bytes);

        try
        {
            using var session = IndexSession.OpenReadOnly(path, bundlesDir);
            if (session.Index.Files.Count == 0 || session.Index.Bundles.Length == 0)
                throw new InvalidDataException("Downloaded file is not a usable _.index.bin");
            AppLog.Info(_paths, $"Downloaded index: {uri} -> {path}");
            return path;
        }
        catch
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
            throw;
        }
    }

    public StatusResult GetStatus(GameLocation game)
    {
        var store = TargetStore.For(game);
        var state = LoadMatchingState(game);
        var currentHash = store.Exists() ? store.HashIndex() : "";
        var isModified = state != null &&
                         currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase);
        var updated = state != null &&
                      !currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase) &&
                      !currentHash.Equals(state.OriginalIndexSha256, StringComparison.OrdinalIgnoreCase);

        return new StatusResult
        {
            Game = game,
            CurrentIndexSha256 = currentHash,
            State = state,
            IsModified = isModified,
            GameUpdated = updated,
            CustomBundlesExist = store.Exists() && store.HasCustomBundles(),
            BackupExists = state != null && File.Exists(state.BackupPath)
        };
    }

    /// <summary>
    /// The persisted state only describes the install it was written for. A state file from a
    /// different target is ignored rather than applied to the wrong game files.
    /// </summary>
    private AppState? LoadMatchingState(GameLocation game)
    {
        var state = StateStore.Load(_paths);
        if (state == null)
            return null;

        if (state.MatchesTarget(game))
            return state;

        AppLog.Warn(_paths,
            $"Ignoring state for a different target ({state.TargetKind} {state.TargetPath}) while working on " +
            $"{game.Kind} {game.TargetPath}.");
        return null;
    }

    public (RulePack Pack, RulePackBuildStats Stats) BuildRules(GameLocation game, string name, string clientBuild)
    {
        using var session = IndexSession.OpenReadOnly(game);
        var paths = session.RuleCandidateFiles().Select(f => f.Path!);
        return RulePackBuilder.Build(paths, name, clientBuild);
    }

    private static PatchPlan BuildPlan(IndexSession session, ApplyRequest request)
    {
        var plan = new PatchPlan
        {
            Preset = request.Preset,
            RulePackName = request.Pack.Name
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var categories = request.CategoryFilter is { Count: > 0 }
            ? request.Pack.Categories.Where(c => request.CategoryFilter.Contains(c.Id))
            : request.Pack.SelectedCategories(request.Preset);

        foreach (var category in categories)
        {
            foreach (var rule in category.Rules)
            {
                if (rule.Experimental && !request.IncludeExperimental)
                {
                    plan.SkippedExperimental.Add(rule.Path);
                    continue;
                }

                if (!seen.Add(rule.Path))
                    continue;

                if (request.Ignore.IsIgnored(rule.Path))
                {
                    plan.Ignored.Add(rule.Path);
                    continue;
                }

                if (!session.TryGetFile(rule.Path, out var record) || record == null)
                {
                    plan.Missing.Add(rule.Path);
                    continue;
                }

                if (rule.Strategy == StrategyKind.Keep)
                {
                    plan.Unsupported.Add(rule.Path);
                    continue;
                }

                // A "change" that writes identical bytes is a no-op: filter it out so the
                // reported count is the number of files that actually change.
                if (rule.Strategy is StrategyKind.StripAttachments or StrategyKind.Auto)
                {
                    var original = record.Read().ToArray();
                    var replacement = ReplacementFactory.Create(original, rule.Path, rule.Strategy);
                    if (replacement.AsSpan().SequenceEqual(original))
                    {
                        plan.Unchanged.Add(rule.Path);
                        continue;
                    }
                }

                plan.Changes.Add(new PlannedChange(
                    rule.Path,
                    rule.Strategy,
                    record.Size,
                    category.Id,
                    rule.Experimental));
            }
        }

        return plan;
    }
}