using System.Text.Json;
using PsCore;
using PsRules;

namespace PsCli.Commands;

internal static class ListRulesCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var packPath = args.Get("pack") ?? (File.Exists(paths.DefaultRulePackPath)
            ? paths.DefaultRulePackPath
            : null);
        if (packPath == null)
        {
            Console.Error.WriteLine("error: no rule pack. Run build-rules first or pass --pack.");
            return 2;
        }

        var pack = RulePackStore.Load(packPath);
        var language = args.Get("language") ?? "zh";

        if (args.HasFlag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pack.Name,
                pack.ClientBuild,
                pack.GeneratedAt,
                pack.RuleCount,
                presets = pack.Presets,
                categories = pack.Categories.Select(c =>
                {
                    var (label, secondary) = c.LabelPair(language);
                    return new
                    {
                        c.Id,
                        c.Group,
                        label,
                        labelSecondary = secondary,
                        // official = copied out of the client's own tables, ours = translated by us,
                        // framed = no Chinese name yet. Lets a diff tell a stale table from a thin one.
                        labelSource = EffectLabels.IsOfficial(c.Id) ? "official"
                            : EffectLabels.Has(c.Id) ? "ours" : "framed",
                        rules = c.Rules.Count,
                        experimental = c.Rules.Count(r => r.Experimental),
                        fx = c.Rules.Count(r => r.Tier == RiskTier.Fx),
                        material = c.Rules.Count(r => r.Tier == RiskTier.Material)
                    };
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"pack       : {pack.Name}");
        Console.WriteLine($"build      : {pack.ClientBuild}");
        Console.WriteLine($"rules      : {pack.RuleCount}");
        Console.WriteLine($"categories : {pack.Categories.Count}");
        Console.WriteLine();
        Console.WriteLine("presets:");
        foreach (var (name, patterns) in pack.Presets)
            Console.WriteLine($"  {name,-8} {string.Join(", ", patterns)}");
        Console.WriteLine();

        foreach (var group in pack.Categories.GroupBy(c => c.Group).OrderBy(g => g.Key))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var category in group.OrderBy(c => c.Id))
            {
                var fx = category.Rules.Count(r => r.Tier == RiskTier.Fx);
                var material = category.Rules.Count(r => r.Tier == RiskTier.Material);
                var suffix = fx + material > 0
                    ? $" ({fx} fx · {material} material)"
                    : "";
                var (label, secondary) = category.LabelPair(language);
                var both = secondary == null ? label : $"{label} / {secondary}";
                Console.WriteLine($"  {category.Id,-34} {category.Rules.Count,6}  {both}{suffix}");
            }
        }

        // The maintenance signal for the two label tables. Splitting official from ours matters:
        // after a client update it is the official rows that can go stale (folder ids change, new
        // skills appear), while ours stay valid translations of names the client never lists.
        var named = pack.Categories.Count(c => EffectLabels.Has(c.Id));
        var official = pack.Categories.Count(c => EffectLabels.IsOfficial(c.Id));
        Console.WriteLine();
        Console.WriteLine($"名称来源 / name provenance:{pack.Categories.Count - named} 类无名字,走框架兜底 / " +
                          $"{named} of {pack.Categories.Count} categories carry a Chinese name — " +
                          $"{official} copied from the client's own tables, {named - official} translated by us " +
                          $"({pack.Categories.Count - named} still framed).");
        return 0;
    }
}