namespace PsCli.Commands;

internal static class DumpPathsCommand
{
    public static int Run(ParsedArgs args)
    {
        var indexPath = ArchiveUtil.NormalizeIndexPath(args.Require("index"));
        if (!File.Exists(indexPath))
        {
            Console.Error.WriteLine($"error: index not found: {indexPath}");
            return 2;
        }

        var bundlesDir = ArchiveUtil.ResolveBundlesDir(args, indexPath);
        var output = Path.GetFullPath(args.Require("out"));
        var filter = args.Get("filter");

        ArchiveUtil.EnsureOodle();
        using var index = ArchiveUtil.OpenReadOnly(indexPath, bundlesDir, out var parseFailures);

        var paths = ArchiveUtil.ParsedFiles(index)
            .Select(f => f.Path!)
            .Where(p => filter == null || p.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (paths.Count == 0)
        {
            Console.Error.WriteLine("error: no paths matched.");
            return 5;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllLines(output, paths);

        Console.WriteLine($"wrote {paths.Count} paths to {output}");
        if (parseFailures != 0)
            Console.Error.WriteLine($"note: {parseFailures} files had no parseable path (tolerated)");

        return 0;
    }
}