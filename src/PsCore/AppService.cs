using System.Net.Http;
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
        using var session = IndexSession.OpenReadOnly(request.Game.IndexPath, request.Game.BundlesDir);
        return BuildPlan(session, request);
    }

    public ApplyResult Apply(ApplyRequest request, IProgress<PatchProgress>? progress = null)
    {
        if (!File.Exists(request.Game.IndexPath))
            throw new FileNotFoundException("PoE2 index not found", request.Game.IndexPath);

        if (GameProcessGuard.IsGameRunning(out var processes) && !request.Force)
            throw new InvalidOperationException(
                $"PoE2 appears to be running ({string.Join(", ", processes)}). Close the game first.");

        _paths.Ensure();
        var state = StateStore.Load(_paths);
        var currentHash = HashUtil.Sha256File(request.Game.IndexPath);

        // Return to a known baseline before rebuilding the patch.
        if (state != null)
        {
            if (currentHash.Equals(state.AppliedIndexSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(state.BackupPath))
                    throw new FileNotFoundException("Backup of the applied state is missing", state.BackupPath);

                BackupManager.RestoreBackup(state.BackupPath, request.Game.IndexPath);
                BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
                currentHash = HashUtil.Sha256File(request.Game.IndexPath);
                StateStore.Delete(_paths);
                state = null;
                AppLog.Info(_paths, "Restored original index from state before applying a new plan.");
            }
            else if (!currentHash.Equals(state.OriginalIndexSha256, StringComparison.OrdinalIgnoreCase))
            {
                // The game was updated (or the index was changed by another tool).
                if (BackupManager.HasCustomBundles(request.Game.BundlesDir) && !request.Force)
                    throw new InvalidOperationException(
                        "Index changed since the last apply (game update?) and custom bundles still exist. " +
                        "Use --force to discard the old state and continue.");

                StateStore.Delete(_paths);
                BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
                state = null;
                AppLog.Warn(_paths, "Index changed since last apply; old state discarded.");
            }
            else
            {
                BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
                StateStore.Delete(_paths);
                state = null;
            }
        }
        else if (BackupManager.HasCustomBundles(request.Game.BundlesDir))
        {
            if (!request.Force)
                throw new InvalidOperationException(
                    "LibGGPK3 custom bundles exist but there is no state file. " +
                    "Restore/clean them first, or use --force to discard them.");
            BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
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

        var backupPath = BackupManager.CreateBackup(request.Game.IndexPath, _paths.BackupsDir);
        AppLog.Info(_paths, $"Backup created: {backupPath}");

        var committed = false;
        try
        {
            using (var work = IndexSession.OpenWorkCopy(request.Game.IndexPath, request.Game.BundlesDir))
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

                    record.Write(replacement);

                    done++;
                    if (done == total || done % reportStep == 0)
                        progress?.Report(new PatchProgress(done, total, change.Path, "write"));
                }

                progress?.Report(new PatchProgress(done, total, "", "save"));
                work.Save();
                work.Commit();
                committed = true;
            }

            var appliedHash = HashUtil.Sha256File(request.Game.IndexPath);
            var newState = new AppState
            {
                GameRoot = request.Game.GameRoot,
                IndexPath = request.Game.IndexPath,
                OriginalIndexSha256 = currentHash,
                AppliedIndexSha256 = appliedHash,
                BackupPath = backupPath,
                RulePackName = request.Pack.Name,
                RulePackPath = request.RulePackPath,
                Preset = request.Preset,
                FileCount = plan.Changes.Count,
                CustomBundleFiles = BackupManager.ListCustomBundleFiles(request.Game.BundlesDir).ToList()
            };
            StateStore.Save(_paths, newState);
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
            BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
            if (committed)
            {
                try
                {
                    BackupManager.RestoreBackup(backupPath, request.Game.IndexPath);
                    AppLog.Warn(_paths, "Apply failed after commit; backup restored.");
                }
                catch (Exception restoreEx)
                {
                    AppLog.Error(_paths, $"Apply rollback failed: {restoreEx}");
                }
            }
            throw;
        }
    }

    public RestoreResult Restore(RestoreRequest request)
    {
        if (GameProcessGuard.IsGameRunning(out var processes) && !request.Force)
            throw new InvalidOperationException(
                $"PoE2 appears to be running ({string.Join(", ", processes)}). Close the game first.");

        var state = StateStore.Load(_paths);
        var currentHash = File.Exists(request.Game.IndexPath)
            ? HashUtil.Sha256File(request.Game.IndexPath)
            : "";

        string? backup = request.BackupPath ?? state?.BackupPath;
        if (!string.IsNullOrWhiteSpace(request.IndexUrl))
            backup = DownloadIndex(request.IndexUrl, request.Game.BundlesDir);
        var cleaned = false;

        if (BackupManager.HasCustomBundles(request.Game.BundlesDir))
        {
            BackupManager.DeleteCustomBundles(request.Game.BundlesDir);
            cleaned = true;
        }

        if (request.CleanOnly)
        {
            StateStore.Delete(_paths);
            return new RestoreResult
            {
                CleanedCustomBundles = cleaned,
                Message = "Custom bundles removed (index unchanged)."
            };
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
            StateStore.Delete(_paths);
            return new RestoreResult
            {
                RestoredIndex = false,
                CleanedCustomBundles = cleaned,
                Message = "Index already matches the backup."
            };
        }

        BackupManager.RestoreBackup(backup, request.Game.IndexPath);
        StateStore.Delete(_paths);
        AppLog.Info(_paths, $"Restored index from backup: {backup}");

        return new RestoreResult
        {
            RestoredIndex = true,
            CleanedCustomBundles = cleaned,
            Message = $"Restored {request.Game.IndexPath}"
        };
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
        var state = StateStore.Load(_paths);
        var currentHash = File.Exists(game.IndexPath) ? HashUtil.Sha256File(game.IndexPath) : "";
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
            CustomBundlesExist = BackupManager.HasCustomBundles(game.BundlesDir),
            BackupExists = state != null && File.Exists(state.BackupPath)
        };
    }

    public (RulePack Pack, RulePackBuildStats Stats) BuildRules(GameLocation game, string name, string clientBuild)
    {
        using var session = IndexSession.OpenReadOnly(game.IndexPath, game.BundlesDir);
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