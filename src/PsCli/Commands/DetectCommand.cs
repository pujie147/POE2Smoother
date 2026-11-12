using System.Text.Json;
using PsCore;

namespace PsCli.Commands;

internal static class DetectCommand
{
    public static int Run(ParsedArgs args)
    {
        IReadOnlyList<GameLocation> candidates;
        var explicitTarget = args.Get("ggpk") ?? args.Get("index") ?? args.Get("game");
        if (!string.IsNullOrWhiteSpace(explicitTarget))
        {
            try
            {
                candidates = [CommandUtil.ResolveGame(args)];
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 3;
            }
        }
        else
        {
            candidates = GameLocator.FindCandidates();
        }

        if (args.HasFlag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(candidates.Select(c => new
            {
                kind = c.Kind.ToString(),
                gameRoot = c.GameRoot,
                targetPath = c.TargetPath,
                bundlesDir = c.BundlesDir,
                indexPath = c.IndexPath,
                ggpkPath = c.GgpkPath,
                exists = File.Exists(c.TargetPath),
                libraryRoot = c.LibraryRoot,
                modified = c.GameVersionHint
            }), new JsonSerializerOptions { WriteIndented = true }));
            return candidates.Count == 0 ? 3 : 0;
        }

        if (candidates.Count == 0)
        {
            Console.Error.WriteLine("No PoE2 client found (Steam library or standalone Content.ggpk).");
            Console.Error.WriteLine("Use --index <Bundles2/_.index.bin>, --ggpk <Content.ggpk> or set POE2_INDEX / POE2_GGPK.");
            return 3;
        }

        Console.WriteLine($"Found {candidates.Count} candidate(s):");
        foreach (var candidate in candidates)
        {
            Console.WriteLine();
            Console.WriteLine($"  kind   : {candidate.Kind}");
            Console.WriteLine($"  game   : {candidate.GameRoot}");
            Console.WriteLine($"  target : {candidate.TargetPath}");
            Console.WriteLine($"  exists : {File.Exists(candidate.TargetPath)}");
            Console.WriteLine($"  mtime  : {candidate.GameVersionHint}");
        }

        return 0;
    }
}