using System.Text.Json;
using PsRules;

namespace PsCore;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh";
    public string? LastTargetPath { get; set; }
    public string LastPreset { get; set; } = "safe";
    public string? LastRulePackPath { get; set; }

    /// <summary>Legacy mirror of <see cref="ExperimentalTier"/>: true whenever any experimental tier is wanted.</summary>
    public bool IncludeExperimental { get; set; }

    /// <summary>
    /// "safe" | "fx" | "material". Stored as a string on purpose: these options objects have no
    /// JsonStringEnumConverter, so an enum property would land in JSON as an integer.
    /// Null means a settings file written before tiers existed — see <see cref="EffectiveTier"/>.
    /// </summary>
    public string? ExperimentalTier { get; set; }

    /// <summary>
    /// Old builds only had the boolean, and it meant "every blanking strategy including .mat",
    /// so a settings file without a tier string migrates to material rather than silently
    /// narrowing what the user had already opted into.
    /// </summary>
    public RiskTier EffectiveTier() =>
        RiskTiers.TryParse(ExperimentalTier, out var tier)
            ? tier
            : IncludeExperimental ? RiskTier.Material : RiskTier.Safe;

    /// <summary>Sets the tier and refreshes the legacy boolean mirror in one step.</summary>
    public void SetTier(RiskTier tier)
    {
        ExperimentalTier = RiskTiers.ToKey(tier);
        IncludeExperimental = tier > RiskTier.Safe;
    }
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