using System.Text.Json;
using PsCore;

namespace PsCli.Commands;

internal static class StatusCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var service = new AppService(paths);
        var status = service.GetStatus(game);

        if (args.HasFlag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                gameRoot = status.Game.GameRoot,
                indexPath = status.Game.IndexPath,
                currentHash = status.CurrentIndexSha256,
                modified = status.IsModified,
                gameUpdated = status.GameUpdated,
                customBundles = status.CustomBundlesExist,
                backupExists = status.BackupExists,
                state = status.State
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"game          : {status.Game.GameRoot}");
        Console.WriteLine($"index         : {status.Game.IndexPath}");
        Console.WriteLine($"index sha256  : {status.CurrentIndexSha256}");
        Console.WriteLine($"modified      : {status.IsModified}");
        Console.WriteLine($"game updated  : {status.GameUpdated}");
        Console.WriteLine($"custom bundles: {status.CustomBundlesExist}");
        Console.WriteLine($"backup exists : {status.BackupExists}");
        if (status.State != null)
        {
            Console.WriteLine($"preset        : {status.State.Preset}");
            Console.WriteLine($"rule pack     : {status.State.RulePackName}");
            Console.WriteLine($"files         : {status.State.FileCount}");
            Console.WriteLine($"applied at    : {status.State.AppliedAt:yyyy-MM-dd HH:mm:ss zzz}");
            Console.WriteLine($"backup        : {status.State.BackupPath}");
        }
        else
        {
            Console.WriteLine("state         : none (original client)");
        }

        return 0;
    }
}