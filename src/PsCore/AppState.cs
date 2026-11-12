using System.Text.Json;

namespace PsCore;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 3;
    public string GameRoot { get; set; } = "";
    public string IndexPath { get; set; } = "";

    /// <summary>Patched file: <c>Bundles2/_.index.bin</c> (Steam) or <c>Content.ggpk</c> (standalone).</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>
    /// <see cref="GameLocationKind"/> name (schema v3). Empty in older state files, which only
    /// ever pointed at Steam installs.
    /// </summary>
    public string TargetKind { get; set; } = "";

    public string OriginalIndexSha256 { get; set; } = "";
    public string AppliedIndexSha256 { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string RulePackName { get; set; } = "";
    public string RulePackPath { get; set; } = "";
    public string Preset { get; set; } = "";
    public int FileCount { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> CustomBundleFiles { get; set; } = [];

    /// <summary>
    /// Per-file fingerprints captured at apply time (schema v2). Restore uses these to
    /// re-redirect a file back to its original bundle coordinates only while the live
    /// content still matches what this tool wrote; game-updated files are skipped.
    /// Older state files deserialize this as an empty list.
    /// </summary>
    public List<PatchedFileEntry> PatchedFiles { get; set; } = [];

    /// <summary>
    /// A state file only ever describes one install: never let a Steam backup be written into a
    /// standalone container, or the other way round. Old (schema &lt;3) states carry no target
    /// fields and keep matching whatever is open, as before.
    /// </summary>
    public bool MatchesTarget(GameLocation game)
    {
        if (!string.IsNullOrEmpty(TargetKind) &&
            !string.Equals(TargetKind, game.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(TargetPath) &&
            !string.Equals(Path.GetFullPath(TargetPath), Path.GetFullPath(game.TargetPath),
                StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}

public sealed class PatchedFileEntry
{
    public string Path { get; set; } = "";
    public string Strategy { get; set; } = "";

    // Where the original content lives in the official bundles (redirect-back target).
    public string OriginalBundlePath { get; set; } = "";
    public long OriginalOffset { get; set; }
    public long OriginalSize { get; set; }

    // Identity of the bytes this tool wrote at apply time (consistency check).
    public long AppliedSize { get; set; }
    public string AppliedSha256 { get; set; } = "";

    // Where the applied content was stored (custom bundle), for diagnostics.
    public string AppliedBundlePath { get; set; } = "";
    public long AppliedOffset { get; set; }
}

public static class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppState? Load(AppPaths paths)
    {
        if (!File.Exists(paths.StatePath))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(paths.StatePath), JsonOptions);
            return state;
        }
        catch
        {
            var corrupt = paths.StatePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try
            {
                File.Move(paths.StatePath, corrupt, overwrite: true);
            }
            catch
            {
                // ignore
            }
            return null;
        }
    }

    public static void Save(AppPaths paths, AppState state)
    {
        paths.Ensure();
        var tmp = paths.StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tmp, paths.StatePath, overwrite: true);
    }

    public static void Delete(AppPaths paths)
    {
        if (File.Exists(paths.StatePath))
            File.Delete(paths.StatePath);
    }
}