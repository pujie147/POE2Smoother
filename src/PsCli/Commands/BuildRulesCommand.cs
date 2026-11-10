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

        var ignore = IgnoreList.FromLines(pack.Ignore);
        ignore.Save(paths.DefaultIgnorePath);

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

        Console.WriteLine($"index      : {game.IndexPath}");
        Console.WriteLine($"scanned    : {stats.PathsScanned}");
        Console.WriteLine($"rules      : {stats.Rules}");
        Console.WriteLine($"categories : {stats.Categories}");
        Console.WriteLine($"skipped    : {stats.Skipped}");
        Console.WriteLine($"written    : {outPath}");
        Console.WriteLine($"ignore     : {paths.DefaultIgnorePath}");
        return 0;
    }
}