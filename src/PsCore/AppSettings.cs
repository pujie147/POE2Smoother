using System.Text.Json;

namespace PsCore;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh";
    public string? LastIndexPath { get; set; }
    public string LastPreset { get; set; } = "safe";
    public string? LastRulePackPath { get; set; }
    public bool IncludeExperimental { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load(AppPaths paths)
    {
        if (!File.Exists(paths.SettingsPath))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsPath), JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppPaths paths, AppSettings settings)
    {
        paths.Ensure();
        var tmp = paths.SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, paths.SettingsPath, overwrite: true);
    }
}