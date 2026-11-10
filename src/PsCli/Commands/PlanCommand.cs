using System.Text.Json;
using PsCore;
using PsRules;

namespace PsCli.Commands;

internal static class PlanCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var pack = CommandUtil.LoadPack(args, paths, game);
        var packPath = args.Get("pack") ?? paths.DefaultRulePackPath;
        var service = new AppService(paths);
        var request = CommandUtil.BuildApplyRequest(args, paths, game, pack, packPath);
        var plan = service.BuildPlan(request);

        var json = JsonSerializer.Serialize(new
        {
            pack = pack.Name,
            request.Preset,
            changes = plan.Changes.Select(c => new
            {
                c.Path,
                strategy = c.Strategy.ToString(),
                c.Size,
                category = c.CategoryId,
                c.Experimental
            }),
            ignored = plan.Ignored,
            missing = plan.Missing,
            unsupported = plan.Unsupported,
            unchanged = plan.Unchanged,
            experimental = plan.SkippedExperimental
        }, new JsonSerializerOptions { WriteIndented = true });

        var outPath = args.Get("out");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, json);
            Console.WriteLine($"plan written: {Path.GetFullPath(outPath)}");
        }

        if (args.HasFlag("json"))
        {
            Console.WriteLine(json);
            return 0;
        }

        Console.WriteLine($"game       : {game.IndexPath}");
        Console.WriteLine($"pack       : {pack.Name}");
        Console.WriteLine($"preset     : {request.Preset}");
        Console.WriteLine($"files      : {plan.Changes.Count}");
        Console.WriteLine($"bytes      : {ArchiveUtil.FormatBytes(plan.TotalBytes)}");
        Console.WriteLine($"ignored    : {plan.Ignored.Count}");
        Console.WriteLine($"missing    : {plan.Missing.Count}");
        Console.WriteLine($"unsupported: {plan.Unsupported.Count}");
        Console.WriteLine($"unchanged  : {plan.Unchanged.Count}");
        Console.WriteLine($"skippedExp : {plan.SkippedExperimental.Count}");
        Console.WriteLine();

        foreach (var change in plan.Changes.Take(30))
            Console.WriteLine($"  {change.Strategy,-20} {ArchiveUtil.FormatBytes(change.Size),10}  {change.Path}");

        if (plan.Changes.Count > 30)
            Console.WriteLine($"  ... {plan.Changes.Count - 30} more");

        return plan.Changes.Count == 0 ? 6 : 0;
    }
}