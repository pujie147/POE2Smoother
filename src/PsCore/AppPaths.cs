namespace PsCore;

public sealed class AppPaths
{
    public string Root { get; }
    public string BackupsDir => Path.Combine(Root, "backups");
    public string ProfilesDir => Path.Combine(Root, "profiles");
    public string LogsDir => Path.Combine(Root, "logs");
    public string RulePacksDir => Path.Combine(Root, "rulepacks");
    public string StatePath => Path.Combine(Root, "state.json");
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string DefaultIgnorePath => Path.Combine(Root, "ignore.txt");
    public string DefaultRulePackPath => Path.Combine(RulePacksDir, "default.json");

    public AppPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? DefaultRoot());
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(RulePacksDir);
    }

    public static string DefaultRoot()
    {
        var custom = Environment.GetEnvironmentVariable("POE2SMOOTHER_HOME");
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(xdg, "poe2-smoother");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "poe2-smoother");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            local = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(local, "poe2-smoother");
    }
}