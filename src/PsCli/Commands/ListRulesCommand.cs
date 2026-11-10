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
                categories = pack.Categories.Select(c => new
                {
                    c.Id,
                    c.Group,
                    label = c.LabelFor(language),
                    rules = c.Rules.Count,
                    experimental = c.Rules.Count(r => r.Experimental)
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
                var experimental = category.Rules.Count(r => r.Experimental);
                var suffix = experimental > 0 ? $" ({experimental} experimental)" : "";
                Console.WriteLine($"  {category.Id,-28} {category.Rules.Count,6}  {category.LabelFor(language)}{suffix}");
            }
        }

        return 0;
    }
}