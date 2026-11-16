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

    /// <summary>
    /// "template" | "empty": what a blanking strategy writes. A settings file from before the choice
    /// existed has nothing here, and "no value" must not mean "go back to writing zero bytes" —
    /// that would silently downgrade every existing install — so the fallback is template.
    /// The property shadows the enum's name inside this class, hence the qualified returns.
    /// </summary>
    public string? BlankStyle { get; set; }

    public PsRules.BlankStyle EffectiveBlankStyle() => PsRules.BlankStyles.FromKey(BlankStyle);

    /// <summary>
    /// "template" | "empty" | "compress-only" | "compress-then-template" | "compress-then-empty":
    /// the user-facing write strategy. <see cref="BlankStyle"/> is kept as a legacy mirror so an
    /// older build still reads a valid blanking style. The new key wins; absent falls back to the
    /// old BlankStyle (never silently downgrading). The property shadows the enum's name inside this
    /// class, hence the qualified returns.
    /// </summary>
    public string? WriteStrategy { get; set; }

    public PsRules.WriteStrategy EffectiveWriteStrategy() =>
        PsRules.WriteStrategies.TryParse(WriteStrategy, out var strategy)
            ? strategy
            : PsRules.WriteStrategies.FromKey(BlankStyle);

    /// <summary>Sets the write strategy and refreshes the legacy BlankStyle mirror in one step.</summary>
    public void SetWriteStrategy(PsRules.WriteStrategy strategy)
    {
        WriteStrategy = PsRules.WriteStrategies.ToKey(strategy);
        BlankStyle = PsRules.BlankStyles.ToKey(PsRules.WriteStrategies.ToBlankStyle(strategy));
    }

    /// <summary>
    /// Rendering-side compression toggles. Only bool/int fields, so plain JSON is safe (no enum
    /// converter needed). Null means a settings file from before compression existed: all off.
    /// </summary>
    public CompressionOptions? Compression { get; set; }

    public CompressionOptions EffectiveCompression() => Compression ?? new CompressionOptions();

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