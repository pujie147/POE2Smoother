using PsCore;

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
            IndexUrl = args.Get("index-url"),
            Force = args.HasFlag("force"),
            CleanOnly = args.HasFlag("clean-only")
        };

        if (!args.HasFlag("yes"))
        {
            var status = service.GetStatus(game);
            Console.WriteLine($"index  : {game.IndexPath}");
            Console.WriteLine($"state  : {(status.State == null ? "none" : status.State.Preset)}");
            Console.WriteLine($"backup : {request.BackupPath ?? status.State?.BackupPath ?? "(unknown)"}");
            Console.WriteLine($"url    : {request.IndexUrl ?? "(none)"}");
            Console.WriteLine($"mode   : {(request.CleanOnly ? "clean custom bundles only" : "restore index")}");
            Console.WriteLine();
            Console.WriteLine("Re-run with --yes to restore.");
            return 0;
        }

        var result = service.Restore(request);
        Console.WriteLine(result.Message);
        if (result.RestoredIndex)
            Console.WriteLine("index restored.");
        if (result.CleanedCustomBundles)
            Console.WriteLine("custom bundles removed.");
        return 0;
    }
}