using System.Text.Json;

namespace PsCore;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public string IndexPath { get; set; } = "";
    public string OriginalIndexSha256 { get; set; } = "";
    public string AppliedIndexSha256 { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string RulePackName { get; set; } = "";
    public string RulePackPath { get; set; } = "";
    public string Preset { get; set; } = "";
    public int FileCount { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> CustomBundleFiles { get; set; } = [];
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