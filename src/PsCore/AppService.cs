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

    /// <summary>
    /// Ceiling for what may be rewritten. Rules whose tier is above it are skipped into the
    /// skip-experimental buckets. The old boolean mapped `true` to <see cref="RiskTier.Material"/>,
    /// which is what "include everything experimental" always meant.
    /// </summary>
    public RiskTier MaxTier { get; init; } = RiskTier.Safe;

    /// <summary>How each file class is rewritten: blank, compress, or compress-first with a blanking
    /// fallback. The single user-facing choice; <see cref="BlankStyle"/> is derived from it.</summary>
    public WriteStrategy WriteStrategy { get; init; } = WriteStrategy.Template;

    /// <summary>What a blanking strategy writes, derived from <see cref="WriteStrategy"/>.</summary>
    public BlankStyle BlankStyle => WriteStrategies.ToBlankStyle(WriteStrategy);

    /// <summary>Opt-in rendering-side compression (textures/shaders/meshes). All off by default;
    /// independent of the blanking preset, but still bounded by <see cref="MaxTier"/>.</summary>
    public CompressionOptions Compression { get; init; } = new();

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

    /// <summary>
    /// Tiers to undo, as a set — not a ceiling: "undo material" must not also undo fx.
    /// Null means everything, which keeps the whole-index backup path available.
    /// </summary>
    public IReadOnlySet<RiskTier>? UndoTiers { get; init; }
    public string? BackupPath { get; init; }
    public string? IndexUrl { get; init; }
    public bool Force { get; init; }
    public bool CleanOnly { get; init; }
}

public sealed class RestoreResult
{
    /// <summary>
    /// The index file itself was replaced from the backup. A per-file restore never sets this:
    /// it redirects records and reports through <see cref="FilesRestored"/> instead, so callers
    /// can tell "the whole index went back" from "some files went back".
    /// </summary>
    public bool RestoredIndex { get; init; }
    public bool CleanedCustomBundles { get; init; }
    public string Message { get; init; } = "";

    // Per-file restore details (only meaningful when PerFile = true).
    public bool PerFile { get; init; }
    public int FilesRestored { get; init; }

    /// <summary>Entries left suppressed by a tier filter (or with no reachable original bundle).</summary>
    public int FilesKept { get; init; }

    /// <summary>True when the caller asked for only some tiers to be undone.</summary>
    public bool Scoped { get; init; }
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

        // Return to a known baseline before rebuilding the patch. A tier-filtered restore leaves a
        // second, later hash that is still ours; treating it as "the game updated" would push the
        // user towards --force, which drops the state and the custom bundles other suppressed
        // files still reference.
        if (state != null)
        {
            if (IsOneOfOurs(currentHash, state))
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

                    // Compression changes carry the exact bytes computed at plan time: a LOD swap
                    // needs a sibling read, and re-running a codec here could only drift from what
                    // the preview counted. The baseline rollback above guarantees the plan was
                    // built against original content.
                    byte[] replacement;
                    if (change.Replacement != null)
                    {
                        replacement = change.Replacement;
                    }
                    else
                    {
                        replacement = change.Strategy is StrategyKind.StripAttachments or StrategyKind.Auto
                            ? ReplacementFactory.Create(record.Read().ToArray(), change.Path, change.Strategy,
                                request.BlankStyle)
                            : ReplacementFactory.Create([], change.Path, change.Strategy, request.BlankStyle);
                    }

                    // Capture the original coordinates before Write() rewrites the record,
                    // so a later per-file restore can redirect back into the official bundle.
                    var entry = new PatchedFileEntry
                    {
                        Path = change.Path,
                        Strategy = change.Strategy.ToString(),
                        Tier = change.Tier.ToString(),
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

            // Verify the committed target before describing it in state.json: a mismatch throws,
            // the catch below restores the backup, and no state file is left claiming a patch that
            // is not actually there. This has to run after Save/Commit — before that, the bytes on
            // disk are still the old index.
            VerifyApplied(patchedFiles, request.Game);

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

    /// <summary>
    /// Which branch <see cref="Restore"/> will take, decided from data the caller already has: the
    /// CLI prints this before restoring, and duplicating the predicate there is how the two drift
    /// apart. A tier filter that leaves something suppressed forces the per-file branch even when
    /// the index has not drifted, because the whole-index path can only restore everything.
    /// </summary>
    public static bool UsesPerFileRestore(string currentHash, AppState? state, RestoreRequest request)
    {
        if (state is not { PatchedFiles.Count: > 0 } || request.Force || request.CleanOnly)
            return false;

        if (!string.IsNullOrWhiteSpace(request.BackupPath) || !string.IsNullOrWhiteSpace(request.IndexUrl))
            return false;

        if (request.UndoTiers is { Count: > 0 } tiers &&
            state.PatchedFiles.Any(entry => !InScope(entry, tiers)))
            return true;

        return !IsOneOfOurs(currentHash, state);
    }

    /// <summary>
    /// The two hashes that mean "this index is what this tool left behind": the one apply produced,
    /// and the one a tier-filtered restore produced afterwards. Empty stored hashes never match —
    /// an index that does not exist hashes as the empty string, and must not read as "ours".
    /// </summary>
    private static bool IsOneOfOurs(string currentHash, AppState state) =>
        (state.AppliedIndexSha256.Length > 0 &&
         currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase)) ||
        (state.PartialRestoreIndexSha256.Length > 0 &&
         currentHash.Equals(state.PartialRestoreIndexSha256, StringComparison.OrdinalIgnoreCase));

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

        // A tier filter selects a subset of what was written; every whole-index mechanism below
        // (backup file, downloaded index, clean-only, --force) overwrites or discards the whole
        // index, so combining them would silently undo suppressions the filter was meant to keep.
        if (request.UndoTiers is { Count: > 0 })
        {
            if (state is not { PatchedFiles.Count: > 0 })
                throw new InvalidOperationException(
                    "A tier filter restores the files this tool recorded in state.json, and there is no " +
                    "such record for this install. Restore everything instead (no --undo-tiers).");

            if (!string.IsNullOrWhiteSpace(request.BackupPath) || !string.IsNullOrWhiteSpace(request.IndexUrl))
                throw new InvalidOperationException(
                    "A tier filter restores per file and cannot be combined with --backup/--index-url, " +
                    "which replace the whole index.");
            if (request.CleanOnly)
                throw new InvalidOperationException(
                    "--clean-only deletes every custom bundle; that would corrupt the files a tier " +
                    "filter is keeping suppressed. Restore without a filter instead.");
            if (request.Force)
                throw new InvalidOperationException(
                    "--force restores the whole backup over the current index, undoing far more than the " +
                    "requested tiers. Drop --force to restore a subset.");
        }

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

        // Safety-first per-file restore (see UsesPerFileRestore for the exact rule): only files
        // whose live content still matches exactly what we wrote get redirected back, so a game
        // update is never downgraded. A tier filter takes this branch too, because redirecting a
        // subset is the only way to leave the rest suppressed.
        if (state is { PatchedFiles.Count: > 0 } &&
            UsesPerFileRestore(currentHash, state, request) &&
            store.Exists())
            return RestorePerFile(state, request.Game, store, request.UndoTiers);

        if (string.IsNullOrWhiteSpace(backup))
            throw new InvalidOperationException("No backup path is known. Run with --backup <file> or apply first.");

        if (!File.Exists(backup))
            throw new FileNotFoundException("Backup not found", backup);

        var backupHash = HashUtil.Sha256File(backup);
        if (state != null && !IsOneOfOurs(currentHash, state))
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
    /// Re-opens the committed target and checks that the index really points at what apply says it
    /// wrote. Two layers, because the realistic failures are systematic (wrong coordinates: every
    /// record) or positional (a truncated tail: the last records of a bundle), and random sampling
    /// catches neither:
    ///   (a) every entry, free — size and bundle/offset straight from the parsed index, no reads;
    ///   (b) content hash on a deterministic bounded set: the first and the last entry written into
    ///       each custom bundle. Those are the two positions where a short write shows up. The set is
    ///       tiny by construction (one pair per touched bundle, and blanked files all share a bundle),
    ///       so verifying templates — which are real bytes now, unlike the old zero-length writes —
    ///       costs a couple of reads, not one per record.
    /// </summary>
    // Public so the negative path (a record that does not match) can be driven from the tests:
    // an integrity check that is only ever exercised when it passes proves nothing.
    public void VerifyApplied(IReadOnlyList<PatchedFileEntry> entries, GameLocation game)
    {
        if (entries.Count == 0)
            return;

        var boundaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var last in entries.GroupBy(e => e.AppliedBundlePath, StringComparer.OrdinalIgnoreCase))
        {
            if (last.FirstOrDefault() is { } first)
                boundaries.Add(first.Path);
            if (last.LastOrDefault() is { } tail)
                boundaries.Add(tail.Path);
        }

        using var read = IndexSession.OpenReadOnly(game);

        var checkedContent = 0;
        foreach (var entry in entries)
        {
            if (!read.TryGetFile(entry.Path, out var record) || record == null)
                throw new InvalidDataException(
                    $"Post-apply verification: {entry.Path} is missing from the committed index.");

            if (record.Size != entry.AppliedSize)
                throw new InvalidDataException(
                    $"Post-apply verification: {entry.Path} is {record.Size} bytes, apply recorded {entry.AppliedSize}.");

            var bundlePath = record.BundleRecord?.Path ?? "";
            if (!string.Equals(bundlePath, entry.AppliedBundlePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Post-apply verification: {entry.Path} points at bundle '{bundlePath}', " +
                    $"apply wrote it into '{entry.AppliedBundlePath}'.");

            if (boundaries.Contains(entry.Path))
            {
                var live = record.Read().ToArray();
                if (HashUtil.Sha256Bytes(live).Equals(entry.AppliedSha256, StringComparison.OrdinalIgnoreCase) == false)
                    throw new InvalidDataException(
                        $"Post-apply verification: {entry.Path} does not contain the bytes apply wrote.");

                checkedContent++;
            }
        }

        AppLog.Info(_paths,
            $"Post-apply verification: {entries.Count} records checked in the committed index, " +
            $"{checkedContent} contents hashed.");
    }

    /// <summary>
    /// Whether a persisted entry belongs to the tiers being undone. An entry whose tier cannot be
    /// determined is never in scope: undoing something unclassifiable is the unrecoverable
    /// direction, leaving it suppressed is not.
    /// </summary>
    private static bool InScope(PatchedFileEntry entry, IReadOnlySet<RiskTier>? undoTiers)
    {
        if (undoTiers is not { Count: > 0 })
            return true;

        return RiskTiers.TierOfEntry(entry.Tier, entry.Strategy, entry.Path) is { } tier &&
               undoTiers.Contains(tier);
    }

    /// <summary>
    /// Restores every file this tool suppressed, but only those still consistent with the bytes
    /// it wrote at apply time: their record is redirected back to the original coordinates in the
    /// official bundle (no data is rewritten, so the custom bundle files become orphans and are
    /// dropped afterwards). Files whose content no longer matches (game update, external edit)
    /// are skipped and keep their current content — that is what prevents post-update downgrades.
    /// With <paramref name="undoTiers"/> set, only entries in those tiers are touched; the rest stay
    /// suppressed and stay in the state file.
    /// </summary>
    private RestoreResult RestorePerFile(AppState state, GameLocation game, TargetStore store,
        IReadOnlySet<RiskTier>? undoTiers = null)
    {
        var restored = 0;
        var skipped = new List<string>();
        var unresolved = new List<PatchedFileEntry>();
        var kept = new List<PatchedFileEntry>();

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
                // Out-of-scope entries are neither redirected nor forgotten: they stay suppressed
                // and stay in the state, so a later restore (or a re-apply) can still account for them.
                if (!InScope(entry, undoTiers))
                {
                    kept.Add(entry);
                    continue;
                }

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

        // Kept = out-of-scope + unresolved. They differ in why, but a later restore has to treat
        // them the same way: still suppressed, still ours, still pointing into a custom bundle.
        var preserved = kept.Count > 0 ? [.. kept, .. unresolved] : unresolved;
        var partialHash = state.PartialRestoreIndexSha256;
        if (restored > 0)
            partialHash = store.HashIndex();

        if (preserved.Count == 0)
        {
            StateStore.Delete(_paths);
            AppLog.Info(_paths,
                $"Per-file restore: restored={restored}, skipped={skipped.Count} (kept current content).");
        }
        else
        {
            // Keep the original/applied hashes so a later restore still recognises this index as
            // ours; PartialRestoreIndexSha256 records the index as this restore left it, so the
            // next apply rolls back to the backup instead of declaring a game update (and being
            // "fixed" with --force, which would delete the bundles these entries live in).
            StateStore.Save(_paths, new AppState
            {
                SchemaVersion = state.SchemaVersion,
                GameRoot = state.GameRoot,
                IndexPath = state.IndexPath,
                TargetPath = state.TargetPath,
                TargetKind = state.TargetKind,
                OriginalIndexSha256 = state.OriginalIndexSha256,
                AppliedIndexSha256 = state.AppliedIndexSha256,
                PartialRestoreIndexSha256 = partialHash,
                BackupPath = state.BackupPath,
                RulePackName = state.RulePackName,
                RulePackPath = state.RulePackPath,
                Preset = state.Preset,
                AppliedAt = state.AppliedAt,
                FileCount = preserved.Count,
                CustomBundleFiles = store.ListCustomBundles().ToList(),
                PatchedFiles = preserved
            });
            AppLog.Warn(_paths,
                $"Per-file restore: restored={restored}, skipped={skipped.Count}, kept={preserved.Count} " +
                $"(out of scope: {kept.Count}, original bundle missing: {unresolved.Count}).");
        }

        var message = undoTiers is { Count: > 0 }
            ? $"Per-file restore of {string.Join("/", undoTiers.OrderBy(t => t))}: " +
              $"{restored} restored, {kept.Count} kept suppressed, {unresolved.Count} unresolvable, " +
              $"{skipped.Count} skipped."
            : unresolved.Count > 0
                ? $"Per-file restore: {restored} restored, {skipped.Count} skipped, {unresolved.Count} kept suppressed (original bundle missing)."
                : $"Per-file restore: {restored} restored, {skipped.Count} skipped (changed by update, kept new content).";
        if (skipped.Count > 0)
        {
            message += " Skipped: " + string.Join(", ", skipped.Take(10)) +
                       (skipped.Count > 10 ? $" (+{skipped.Count - 10} more)" : "");
        }

        return new RestoreResult
        {
            RestoredIndex = false,
            CleanedCustomBundles = cleanedBundles,
            PerFile = true,
            FilesRestored = restored,
            FilesSkipped = skipped.Count,
            FilesUnresolved = unresolved.Count,
            FilesKept = kept.Count,
            Scoped = undoTiers is { Count: > 0 },
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
        // "Ours" is either hash: what apply produced, or what a tier-filtered restore left behind.
        var isOurs = state != null && IsOneOfOurs(currentHash, state);
        var isModified = isOurs;
        var updated = state != null && !isOurs &&
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

        var compressionOn = WriteStrategies.IsCompressionContaining(request.WriteStrategy);
        var blankingOff = request.WriteStrategy == WriteStrategy.CompressionOnly;

        foreach (var category in categories)
        {
            foreach (var rule in category.Rules)
            {
                // CompressionOnly never blanks. Otherwise blanking owns every file the compression
                // pass will not actually visit: a candidate is root+extension+toggle aware, so a
                // metadata .dds (a blanking root, outside the texture-compression root) still blanks
                // instead of being silently dropped by a whole-extension skip.
                if (blankingOff)
                    continue;
                if (compressionOn && CompressionPlanner.IsCandidate(rule.Path, request.Compression))
                    continue;

                // Compression strategies are planner-only (they need original bytes / siblings);
                // a hand-written pack rule naming one is not something the blanking path can run.
                if (CompressionOptions.IsCompressionStrategy(rule.Strategy))
                {
                    plan.Unsupported.Add(rule.Path);
                    continue;
                }

                // Tier ceiling, checked before dedup/ignore/missing so the skip buckets stay
                // comparable with the "attempted" totals the UI sums up.
                if (rule.Tier > request.MaxTier)
                {
                    plan.SkippedExperimental.Add(rule.Path);
                    if (rule.Tier == RiskTier.Material)
                        plan.SkippedMaterialCount++;
                    else
                        plan.SkippedFxCount++;
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
                // `Auto` is resolved here exactly the way ReplacementFactory.Create resolves it, so a
                // hand-written Auto rule blanks through the same template path as a typed one.
                var strategy = rule.Strategy;
                if (strategy == StrategyKind.Auto)
                    RulePackBuilder.TryGetStrategy(rule.Path, out strategy);

                var blank = ReplacementFactory.BlankReplacement(strategy, request.BlankStyle);
                if (blank != null)
                {
                    // Preview paths (pscli plan, apply without --yes, the GUI's material dialog) build
                    // a plan against the *live* index, which is already blanked after an apply. Without
                    // this they would report re-writing a template over the same template as a change —
                    // inflating the counts the confirmation dialog shows and turning `plan`'s "nothing
                    // to do" exit code into a lie.
                    //
                    // "Already blank" is keyed on *who wrote those bytes*, not on what they look like:
                    // an official file that happens to be exactly as long as one of our templates must
                    // still be rewritten. During Apply the baseline rollback runs first, so originals
                    // are back in official bundles and real writes are never filtered out; a patch left
                    // by an older build (0-byte stubs under the template style) does not match either,
                    // so upgrading re-blanks those files instead of reporting "nothing to do".
                    if (record.Size == blank.Length &&
                        BackupManager.IsCustomBundlePath(record.BundleRecord?.Path))
                    {
                        plan.Unchanged.Add(rule.Path);
                        continue;
                    }
                }
                else if (strategy is StrategyKind.StripAttachments or StrategyKind.Auto or StrategyKind.Keep)
                {
                    var original = record.Read().ToArray();
                    var replacement = ReplacementFactory.Create(original, rule.Path, rule.Strategy,
                        request.BlankStyle);
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
                    rule.Tier,
                    rule.Tier > RiskTier.Safe));
            }
        }

        CompressionPlanner.AddChanges(plan, session, request, seen);
        return plan;
    }
}