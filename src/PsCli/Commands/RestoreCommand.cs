using PsCore;
using PsRules;

namespace PsCli.Commands;

internal static class RestoreCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var service = new AppService(paths);
        var request = new RestoreRequest
        {
            Game = game,
            BackupPath = args.Get("backup"),
            UndoTiers = CommandUtil.ResolveUndoTiers(args),
            IndexUrl = args.Get("index-url"),
            Force = args.HasFlag("force"),
            CleanOnly = args.HasFlag("clean-only")
        };

        if (!args.HasFlag("yes"))
        {
            var status = service.GetStatus(game);
            Console.WriteLine($"target : {game.TargetPath}");
            Console.WriteLine($"state  : {(status.State == null ? "none" : status.State.Preset)}");
            Console.WriteLine($"backup : {request.BackupPath ?? status.State?.BackupPath ?? "(unknown)"}");
            Console.WriteLine($"url    : {request.IndexUrl ?? "(none)"}");
            var perFile = AppService.UsesPerFileRestore(status.CurrentIndexSha256, status.State, request);
            var scope = request.UndoTiers is { Count: > 0 } tiers
                ? " [tiers: " + string.Join(",", tiers.OrderBy(t => t)) + "]"
                : "";
            Console.WriteLine($"mode   : {(request.CleanOnly ? "clean custom bundles only" : perFile ? "per-file safe restore (files changed by a game update are skipped)" : "restore index from backup")}{scope}");
            Console.WriteLine();
            Console.WriteLine("Re-run with --yes to restore.");
            return 0;
        }

        var result = service.Restore(request);
        Console.WriteLine(result.Message);
        if (result.PerFile)
        {
            Console.WriteLine($"per-file: restored={result.FilesRestored} skipped={result.FilesSkipped} " +
                              $"unresolved={result.FilesUnresolved}" +
                              (result.Scoped ? $" kept={result.FilesKept}" : ""));
            foreach (var path in result.SkippedPaths.Take(10))
                Console.WriteLine($"  kept (changed since apply): {path}");
        }
        else if (result.RestoredIndex)
            Console.WriteLine("index restored.");
        if (result.CleanedCustomBundles)
            Console.WriteLine("custom bundles removed.");
        return 0;
    }
}