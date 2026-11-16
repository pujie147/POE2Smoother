using System.Text.Json;
using System.Text.Json.Serialization;

namespace PsRules;

public enum StrategyKind
{
    Auto,
    Empty,
    StripAttachments,
    TransparentDds,
    Keep,
    NullPet,
    NullMaterial,
    NullGraph,
    NullEffectPack,

    // Rendering-side compression (opt-in via CompressionOptions, never the Auto default):
    // keeps the visual role, lowers render cost. See docs/experimental-risk-tiers.md §5.
    CompressDds,
    SimplifyGraph,
    SwapMeshLod,
    DecimateMesh
}

/// <summary>
/// How much the tool is willing to touch for one rule. The old single "experimental" tick
/// bundled every blanking strategy together, including <see cref="StrategyKind.NullMaterial"/>,
/// blanks the materials ordinary rendering shares. Splitting them lets a user take the
/// effect-only win without that, and lets a partial restore undo one without the other.
/// </summary>
public enum RiskTier
{
    /// <summary>Script surgery on `.ao`, or a valid 1x1 transparent stub for `.dds`. Always applied.</summary>
    Safe,

    /// <summary>Blank an effect resource (`.pet`/`.epk`/`.fxgraph`/`.aoc`); see <see cref="BlankTemplates"/>.</summary>
    Fx,

    /// <summary>Blank a material (`.mat`). Referenced by rendering outside the effect pack, so it is gated hardest.</summary>
    Material
}

public static class RiskTiers
{
    /// <summary>
    /// Tier implied by the replacement itself. `Auto` resolves through the extension table because
    /// <see cref="ReplacementFactory"/> only resolves it at write time; an extension that has no
    /// strategy is not gated at all — BuildPlan buckets it as unsupported before anything is written.
    /// </summary>
    public static RiskTier Of(StrategyKind strategy, string path)
    {
        var resolved = strategy;
        if (resolved == StrategyKind.Auto &&
            !RulePackBuilder.TryGetStrategy(path, out resolved))
            return RiskTier.Safe;

        return resolved switch
        {
            // Textures are shared with ordinary rendering everywhere, so recompressing them is
            // gated as hard as blanking materials. Mesh/shader compression stays in the fx tier.
            StrategyKind.NullMaterial or StrategyKind.CompressDds => RiskTier.Material,
            StrategyKind.NullPet or StrategyKind.NullEffectPack or StrategyKind.NullGraph
                or StrategyKind.Empty or StrategyKind.SimplifyGraph or StrategyKind.SwapMeshLod
                or StrategyKind.DecimateMesh => RiskTier.Fx,
            _ => RiskTier.Safe
        };
    }

    /// <summary>
    /// A hand-written pack can mark any rule experimental; honour that as a floor so upgrading
    /// never starts writing files the previous build would have skipped.
    /// </summary>
    public static RiskTier OfRule(Rule rule) =>
        (RiskTier)Math.Max((int)Of(rule.Strategy, rule.Path),
                           (int)(rule.Experimental ? RiskTier.Fx : RiskTier.Safe));

    public static string ToKey(RiskTier tier) => tier switch
    {
        RiskTier.Material => "material",
        RiskTier.Fx => "fx",
        _ => "safe"
    };

    /// <summary>
    /// Tier of a record already written to state.json. Strategy names there are stored with
    /// <c>ToString()</c> (PascalCase), not the kebab-case the pack JSON uses, and a hand-written
    /// <c>Auto</c> rule never got resolved before it was persisted — so the path is needed too.
    /// Returns null for anything unrecognised: an unknown tier must never be swept up by a
    /// restore filter, because "keep it suppressed" is the recoverable choice and "undo it" is not.
    /// </summary>
    public static RiskTier? TierOfEntry(string? tierName, string? strategyName, string path)
    {
        if (ParseTierName(tierName) is { } stored)
            return stored;

        if (Enum.TryParse<StrategyKind>(strategyName, ignoreCase: true, out var parsed) &&
            parsed != StrategyKind.Auto)
            return Of(parsed, path);

        // No tier column (state written before tiers existed) plus "Auto" or junk in the strategy
        // column: fall back to what the extension implies.
        return RulePackBuilder.TryGetStrategy(path, out var byExtension)
            ? Of(byExtension, path)
            : null;
    }

    public static RiskTier? ParseTierName(string? value) =>
        Enum.TryParse<RiskTier>(value, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>
    /// Tiers named by a CLI list (`--undo-tiers safe,fx`). Unlike the apply ceiling this is a plain
    /// set — "undo material" must not also undo fx. Unknown names are rejected by the caller.
    /// </summary>
    public static bool TryParseTierList(string? value, out HashSet<RiskTier> tiers)
    {
        tiers = [];
        foreach (var token in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParse(token, out var tier))
                return false;

            tiers.Add(tier);
        }

        return tiers.Count > 0;
    }

    /// <summary>Parses the settings/profile/CLI vocabulary. `off` is the same as `safe`.</summary>
    public static bool TryParse(string? value, out RiskTier tier)
    {
        tier = value?.Trim().ToLowerInvariant() switch
        {
            "material" => RiskTier.Material,
            "fx" => RiskTier.Fx,
            "safe" or "off" or "" => RiskTier.Safe,
            _ => (RiskTier)(-1)
        };
        return tier is RiskTier.Safe or RiskTier.Fx or RiskTier.Material;
    }
}

public sealed class Rule
{
    public string Path { get; set; } = "";
    public StrategyKind Strategy { get; set; } = StrategyKind.Auto;
    public bool Experimental { get; set; }
    public string? Note { get; set; }

    /// <summary>Derived, never serialized: the pack schema stays at version 1 so old packs still load.</summary>
    [JsonIgnore]
    public RiskTier Tier => RiskTiers.OfRule(this);
}

public sealed class RuleCategory
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public Dictionary<string, string> Label { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Rule> Rules { get; set; } = [];

    /// <summary>
    /// zh comes from <see cref="EffectLabels"/> first so a wording fix reaches rule packs that are
    /// already on disk — rebuilding one means re-scanning the GGPK, which is not a price worth
    /// charging for a label. en still comes from the pack, because that is the built artifact.
    /// </summary>
    public string LabelFor(string language)
    {
        if (IsEnglish(language) && Label.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english))
            return english;

        if (EffectLabels.TryGet(Id, out var shipped))
            return shipped;

        if (Label.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        if (Label.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
            return en;
        return Id;
    }

    /// <summary>
    /// Both languages for one row, ordered by the current UI language. The secondary language is
    /// null when it would repeat the primary — packs built before the label table carry identical
    /// zh/en strings for most skills, and "Fire / Fire" reads like a bug.
    /// </summary>
    public (string Primary, string? Secondary) LabelPair(string language)
    {
        var primary = LabelFor(language);
        var secondary = LabelFor(IsEnglish(language) ? "zh" : "en");

        // LabelFor falls through to the id when a pack has no label at all; a bare id duplicated on
        // both sides is noise, not information.
        if (string.IsNullOrWhiteSpace(secondary) || secondary == primary || CarriesSameName(primary, secondary))
            return (primary, null);

        return (primary, secondary);
    }

    /// <summary>
    /// True when the other side only repeats the same name. Most skill rows are labelled
    /// "中文前缀 + 英文原名" — "技能: Crossbow Ballistas" — so printing the English again would show
    /// the same words twice on the ~650 categories the label table has not learned yet. Compared on
    /// the latin core so the Chinese frame and the English frame of one name cancel out, while a real
    /// translation ("火焰" vs "Fire", whose cores are empty and unequal alike) still shows both.
    /// </summary>
    private static bool CarriesSameName(string primary, string secondary)
    {
        var a = LatinCore(primary);
        var b = LatinCore(secondary);
        if (a.Length == 0 || b.Length == 0)
            return false;

        return a == b || b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal);
    }

    private static string LatinCore(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                buffer.Append(char.ToLowerInvariant(c));
        }

        return buffer.ToString();
    }

    private static bool IsEnglish(string language) =>
        language.Equals("en", StringComparison.OrdinalIgnoreCase);
}

public sealed class RulePack
{
    public int SchemaVersion { get; set; } = 1;
    public string Game { get; set; } = "poe2";
    public string ClientBuild { get; set; } = "unknown";
    public string Name { get; set; } = "PoE2 Core Effects";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<RuleCategory> Categories { get; set; } = [];
    public List<string> Ignore { get; set; } = [];
    public Dictionary<string, List<string>> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int RuleCount => Categories.Sum(c => c.Rules.Count);

    public IEnumerable<RuleCategory> SelectedCategories(string preset)
    {
        if (!Presets.TryGetValue(preset, out var patterns))
            patterns = ["*"];

        return Categories.Where(c => patterns.Any(p => PresetSelector.Matches(c.Id, p)));
    }

    /// <summary>
    /// Same ceiling rule as <c>AppService.BuildPlan</c>: a rule is selected when its tier does not
    /// exceed <paramref name="maxTier"/>. Kept in sync deliberately so a preview built here can never
    /// disagree with the plan that actually gets written.
    /// </summary>
    public IEnumerable<Rule> SelectedRules(string preset, RiskTier maxTier)
    {
        foreach (var category in SelectedCategories(preset))
        {
            foreach (var rule in category.Rules)
            {
                if (rule.Tier > maxTier)
                    continue;
                yield return rule;
            }
        }
    }

    public RulePack Clone()
    {
        var json = JsonSerializer.Serialize(this, RulePackStore.JsonOptions);
        return JsonSerializer.Deserialize<RulePack>(json, RulePackStore.JsonOptions)
               ?? throw new InvalidDataException("Could not clone rule pack.");
    }
}

public static class PresetSelector
{
    public static bool Matches(string id, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        pattern = pattern.Trim();
        if (pattern == "*")
            return true;

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^2];
            return id.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return id.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

public static class RulePackStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public static RulePack Load(string path)
    {
        var json = File.ReadAllText(path);
        var pack = JsonSerializer.Deserialize<RulePack>(json, JsonOptions)
                   ?? throw new InvalidDataException($"Could not parse rule pack: {path}");
        if (pack.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported rule pack schema: {pack.SchemaVersion}");
        return pack;
    }

    public static void Save(RulePack pack, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(pack, JsonOptions));
    }
}