using PsCore;

namespace PsCli.Commands;

internal static class BackupsCommand
{
    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        paths.Ensure();

        if (args.HasFlag("prune"))
        {
            BackupManager.Prune(paths.BackupsDir, 10);
            Console.WriteLine($"pruned backups in {paths.BackupsDir} (kept 10)");
            return 0;
        }

        var files = Directory.Exists(paths.BackupsDir)
            ? Directory.EnumerateFiles(paths.BackupsDir, "*.bin")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList()
            : [];

        Console.WriteLine($"backups: {paths.BackupsDir}");
        foreach (var file in files)
            Console.WriteLine($"  {file.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}  {ArchiveUtil.FormatBytes(file.Length),10}  {file.Name}");

        return 0;
    }
}