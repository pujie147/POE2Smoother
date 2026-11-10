using System.Text.RegularExpressions;

namespace PsRules;

public sealed record RulePackBuildStats(int PathsScanned, int Rules, int Categories, int Skipped);

public static class RulePackBuilder
{
    private static readonly Dictionary<string, (string Zh, string En)> LeagueLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abyss"] = ("深渊", "Abyss"),
        ["azmeri"] = ("阿兹梅里", "Azmeri"),
        ["breach"] = ("裂痕", "Breach"),
        ["delirium"] = ("迷雾", "Delirium"),
        ["expedition"] = ("探险", "Expedition"),
        ["incursion"] = ("入侵", "Incursion"),
        ["ritual"] = ("仪式", "Ritual"),
        ["trials"] = ("试炼", "Trials"),
        ["corruption"] = ("腐化", "Corruption")
    };

    private static readonly Dictionary<string, (string Zh, string En)> SkillLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fire"] = ("火焰", "Fire"),
        ["cold"] = ("冰霜", "Cold"),
        ["lightning"] = ("闪电", "Lightning"),
        ["physical"] = ("物理", "Physical"),
        ["chaos"] = ("混沌", "Chaos"),
        ["minions"] = ("召唤", "Minions"),
        ["general"] = ("通用", "General"),
        ["support"] = ("辅助", "Support"),
        ["monsters_effects"] = ("怪物技能", "Monster skills")
    };

    public static (RulePack Pack, RulePackBuildStats Stats) Build(
        IEnumerable<string> paths,
        string name = "PoE2 Core Effects",
        string clientBuild = "unknown",
        IgnoreList? ignore = null)
    {
        var categories = new Dictionary<string, RuleCategory>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        var skipped = 0;

        foreach (var path in paths)
        {
            scanned++;
            if (!IsSupportedRoot(path))
            {
                skipped++;
                continue;
            }

            if (!TryGetStrategy(path, out var strategy, out var experimental))
            {
                skipped++;
                continue;
            }

            if (!seen.Add(path))
                continue;

            var (categoryId, group, labelZh, labelEn) = Classify(path);
            if (!categories.TryGetValue(categoryId, out var category))
            {
                category = new RuleCategory
                {
                    Id = categoryId,
                    Group = group,
                    Label = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["zh"] = labelZh,
                        ["en"] = labelEn
                    }
                };
                categories.Add(categoryId, category);
            }

            category.Rules.Add(new Rule
            {
                Path = path.Replace('\\', '/'),
                Strategy = strategy,
                Experimental = experimental
            });
        }

        var pack = new RulePack
        {
            Name = name,
            ClientBuild = clientBuild,
            Categories = categories.Values
                .OrderBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Ignore = (ignore ?? IgnoreList.Default()).Prefixes.ToList(),
            Presets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["safe"] = ["skill.*", "mtx.*"],
                ["clean"] = ["skill.*", "mtx.*", "env.*", "league.*"],
                ["performance"] = ["league.*", "env.*"],
                ["full"] = ["*"]
            }
        };

        foreach (var category in pack.Categories)
            category.Rules.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        var stats = new RulePackBuildStats(scanned, pack.RuleCount, pack.Categories.Count, skipped);
        return (pack, stats);
    }

    public static bool TryGetStrategy(string path, out StrategyKind strategy, out bool experimental)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ao":
                strategy = StrategyKind.StripAttachments;
                experimental = false;
                return true;
            case ".dds":
                strategy = StrategyKind.TransparentDds;
                experimental = false;
                return true;
            case ".pet":
                strategy = StrategyKind.NullPet;
                experimental = true;
                return true;
            case ".epk":
                strategy = StrategyKind.NullEffectPack;
                experimental = true;
                return true;
            case ".mat":
                strategy = StrategyKind.NullMaterial;
                experimental = true;
                return true;
            case ".fxgraph":
                strategy = StrategyKind.NullGraph;
                experimental = true;
                return true;
            case ".aoc":
                strategy = StrategyKind.Empty;
                experimental = true;
                return true;
            default:
                strategy = StrategyKind.Keep;
                experimental = false;
                return false;
        }
    }

    private static bool IsSupportedRoot(string path)
        => path.StartsWith("metadata/effects/", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("metadata/terrain/leagues/", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("metadata/materials/environment/", StringComparison.OrdinalIgnoreCase);

    private static (string Id, string Group, string Zh, string En) Classify(string path)
    {
        var lower = path.ToLowerInvariant();

        var league = LeagueFromPath(lower);
        if (league != null)
        {
            var labels = LeagueLabels[league];
            return ($"league.{league}", "leagues", labels.Zh, labels.En);
        }

        const string materialsEnv = "metadata/materials/environment/";
        if (lower.StartsWith(materialsEnv, StringComparison.Ordinal))
        {
            var seg = FirstSegment(lower[materialsEnv.Length..]);
            return ($"env.{Slug(seg)}", "environment", "环境材质: " + Pretty(seg), "Environment material: " + Pretty(seg));
        }

        const string terrainLeagues = "metadata/terrain/leagues/";
        if (lower.StartsWith(terrainLeagues, StringComparison.Ordinal))
        {
            var seg = FirstSegment(lower[terrainLeagues.Length..]);
            return ($"terrain.{Slug(seg)}", "environment", "地形: " + Pretty(seg), "Terrain: " + Pretty(seg));
        }

        const string spells = "metadata/effects/spells/";
        if (lower.StartsWith(spells, StringComparison.Ordinal))
        {
            var rest = lower[spells.Length..];
            if (rest.StartsWith("monsters_effects", StringComparison.Ordinal))
                return ("monsters", "monsters", "怪物技能", "Monster skills");

            var seg = FirstSegment(rest);
            if (SkillLabels.TryGetValue(seg, out var labels))
                return ($"skill.{Slug(seg)}", "skills", labels.Zh, labels.En);
            return ($"skill.{Slug(seg)}", "skills", Pretty(seg), Pretty(seg));
        }

        const string mtx = "metadata/effects/microtransactions/";
        if (lower.StartsWith(mtx, StringComparison.Ordinal))
        {
            var seg = FirstSegment(lower[mtx.Length..]);
            return ($"mtx.{Slug(seg)}", "mtx", "外观特效: " + Pretty(seg), "MTX: " + Pretty(seg));
        }

        const string env = "metadata/effects/environment/";
        const string weather = "metadata/effects/weather_attachments/";
        if (lower.StartsWith(env, StringComparison.Ordinal) ||
            lower.StartsWith(weather, StringComparison.Ordinal))
        {
            var prefix = lower.StartsWith(env, StringComparison.Ordinal) ? env : weather;
            var seg = FirstSegment(lower[prefix.Length..]);
            return ($"env.{Slug(seg)}", "environment", "环境: " + Pretty(seg), "Environment: " + Pretty(seg));
        }

        return ("misc", "misc", "其他特效", "Misc effects");
    }

    private static string? LeagueFromPath(string lower)
    {
        if (lower.Contains("delirium", StringComparison.Ordinal) ||
            lower.Contains("league_affliction", StringComparison.Ordinal) ||
            lower.Contains("guidingfog", StringComparison.Ordinal))
            return "delirium";
        if (lower.Contains("azmeri", StringComparison.Ordinal) ||
            lower.Contains("guidinglight", StringComparison.Ordinal) ||
            lower.Contains("wisp_sacred", StringComparison.Ordinal))
            return "azmeri";
        if (lower.Contains("breach", StringComparison.Ordinal))
            return "breach";
        if (lower.Contains("expedition", StringComparison.Ordinal))
            return "expedition";
        if (lower.Contains("incursion", StringComparison.Ordinal))
            return "incursion";
        if (lower.Contains("ritual", StringComparison.Ordinal))
            return "ritual";
        if (lower.Contains("trial", StringComparison.Ordinal))
            return "trials";
        if (lower.Contains("corruption", StringComparison.Ordinal))
            return "corruption";
        if (lower.Contains("abyss", StringComparison.Ordinal))
            return "abyss";
        return null;
    }

    private static string FirstSegment(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "other" : slug;
    }

    private static string Pretty(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Other";
        var text = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }
}