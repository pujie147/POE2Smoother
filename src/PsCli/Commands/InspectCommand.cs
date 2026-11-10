using System.Text.Json;
using LibBundle3;

namespace PsCli.Commands;

internal static class InspectCommand
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
        var filter = args.Get("filter");
        var limit = int.TryParse(args.Get("limit"), out var parsedLimit) ? parsedLimit : 30;
        var asJson = args.HasFlag("json");

        ArchiveUtil.EnsureOodle();

        using var index = ArchiveUtil.OpenReadOnly(indexPath, bundlesDir, out var parseFailures);
        var allFiles = index.Files.Values.ToList();
        var parsed = ArchiveUtil.ParsedFiles(index).ToList();
        var effects = ArchiveUtil.Effects(index).ToList();

        var sample = (filter == null ? effects : parsed.Where(f =>
                f.Path!.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .Take(limit)
            .Select(f => new
            {
                path = f.Path,
                size = f.Size,
                bundle = f.BundleRecord.Path
            })
            .ToList();

        var groups = parsed
            .GroupBy(f => TopGroup(f.Path!), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => new { group = g.Key, count = g.Count() })
            .ToList();

        if (asJson)
        {
            var payload = new
            {
                index = indexPath,
                bundles = bundlesDir,
                parseFailures,
                bundlesCount = index.Bundles.Length,
                filesCount = allFiles.Count,
                parsedCount = parsed.Count,
                effectsCount = effects.Count,
                groups,
                sample
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return parseFailures == 0 ? 0 : 4;
        }

        Console.WriteLine($"index          : {indexPath}");
        Console.WriteLine($"bundles        : {bundlesDir}");
        Console.WriteLine($"bundles count  : {index.Bundles.Length}");
        Console.WriteLine($"files          : {allFiles.Count}");
        Console.WriteLine($"parsed paths   : {parsed.Count}");
        Console.WriteLine($"path failures  : {parseFailures}");
        Console.WriteLine($"effect paths   : {effects.Count}");
        Console.WriteLine();

        Console.WriteLine("top groups:");
        foreach (var group in groups)
            Console.WriteLine($"  {group.count,8}  {group.group}");
        Console.WriteLine();

        Console.WriteLine($"sample ({(filter == null ? "metadata/effects/" : filter)}):");
        foreach (var item in sample)
            Console.WriteLine($"  {ArchiveUtil.FormatBytes(item.size),10}  {item.path}");
        Console.WriteLine();

        if (parseFailures != 0)
        {
            Console.Error.WriteLine($"note: {parseFailures} files had no parseable path (tolerated).");
            Console.Error.WriteLine("      PoE2 indexes commonly have a few pathless records; effect paths above are still valid.");
        }

        if (effects.Count == 0)
        {
            Console.Error.WriteLine("WARNING: no metadata/effects/ paths were found.");
            Console.Error.WriteLine("         The index may be for a different client, or path parsing is wrong.");
            return 5;
        }

        return 0;
    }

    private static string TopGroup(string path)
    {
        var parts = path.Split('/');
        if (parts.Length <= 2)
            return string.Join('/', parts);
        return string.Join('/', parts.Take(3));
    }
}