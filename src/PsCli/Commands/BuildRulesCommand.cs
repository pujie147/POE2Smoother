using System.Text.Json;
using PsCore;
using PsRules;

namespace PsCli.Commands;

internal static class BuildRulesCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var name = args.Get("name") ?? "PoE2 Core Effects";
        var build = args.Get("build") ?? "unknown";
        var outPath = Path.GetFullPath(args.Get("out") ?? paths.DefaultRulePackPath);

        var service = new AppService(paths);
        var (pack, stats) = service.BuildRules(game, name, build);
        RulePackStore.Save(pack, outPath);

        // Merged, never overwritten. pack.Ignore is always the built-in default list (AppService.BuildRules
        // passes no IgnoreList), and this file is where the GUI's ground-effect tree keeps its state —
        // rewriting it here would silently un-protect whatever the player had ticked.
        var existing = File.Exists(paths.DefaultIgnorePath) ? File.ReadAllText(paths.DefaultIgnorePath) : "";
        var merged = GroundEffectProtection.AppendMissing(existing, pack.Ignore);
        File.WriteAllText(paths.DefaultIgnorePath, merged.EndsWith('\n') ? merged : merged + "\n");

        if (args.HasFlag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                output = outPath,
                stats.PathsScanned,
                stats.Rules,
                stats.Categories,
                stats.Skipped,
                presets = pack.Presets
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"target     : {game.TargetPath}");
        Console.WriteLine($"scanned    : {stats.PathsScanned}");
        Console.WriteLine($"rules      : {stats.Rules}");
        Console.WriteLine($"categories : {stats.Categories}");
        Console.WriteLine($"skipped    : {stats.Skipped}");
        Console.WriteLine($"written    : {outPath}");
        Console.WriteLine($"ignore     : {paths.DefaultIgnorePath}");
        return 0;
    }
}