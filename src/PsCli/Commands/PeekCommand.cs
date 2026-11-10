namespace PsCli.Commands;

internal static class PeekCommand
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
        ArchiveUtil.EnsureOodle();

        using var index = ArchiveUtil.OpenNoPaths(indexPath, bundlesDir);
        Console.WriteLine($"index        : {indexPath}");
        Console.WriteLine($"bundles      : {bundlesDir}");
        Console.WriteLine($"bundles count: {index.Bundles.Length}");
        Console.WriteLine($"files        : {index.Files.Count}");

        try
        {
            var failures = index.ParsePaths();
            var parsed = ArchiveUtil.ParsedFiles(index).Count();
            Console.WriteLine($"parsed paths : {parsed}");
            Console.WriteLine($"path failures: {failures}");
            if (failures != 0)
                Console.Error.WriteLine($"note: {failures} files had no parseable path (tolerated).");

            return parsed > 0 ? 0 : 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"path parsing failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("The index loaded, but its path table is not understood by this LibGGPK3 build.");
            Console.Error.WriteLine("This is the main PoE2 compatibility check for M0.");
            return 4;
        }
    }
}