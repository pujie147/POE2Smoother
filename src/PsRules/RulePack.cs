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
    NullEffectPack
}

public sealed class Rule
{
    public string Path { get; set; } = "";
    public StrategyKind Strategy { get; set; } = StrategyKind.Auto;
    public bool Experimental { get; set; }
    public string? Note { get; set; }
}

public sealed class RuleCategory
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public Dictionary<string, string> Label { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Rule> Rules { get; set; } = [];

    public string LabelFor(string language)
    {
        if (Label.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        if (Label.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
            return en;
        return Id;
    }
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

    public IEnumerable<Rule> SelectedRules(string preset, bool includeExperimental)
    {
        foreach (var category in SelectedCategories(preset))
        {
            foreach (var rule in category.Rules)
            {
                if (rule.Experimental && !includeExperimental)
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