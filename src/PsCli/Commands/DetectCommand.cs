using System.Text.Json;
using PsCore;

namespace PsCli.Commands;

internal static class DetectCommand
{
    public static int Run(ParsedArgs args)
    {
        var candidates = SteamLibraryLocator.FindCandidates();
        if (args.HasFlag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(candidates.Select(c => new
            {
                gameRoot = c.GameRoot,
                bundlesDir = c.BundlesDir,
                indexPath = c.IndexPath,
                exists = c.IsSteamBundles,
                libraryRoot = c.LibraryRoot,
                modified = c.GameVersionHint
            }), new JsonSerializerOptions { WriteIndented = true }));
            return candidates.Count == 0 ? 3 : 0;
        }

        if (candidates.Count == 0)
        {
            Console.Error.WriteLine("No PoE2 Steam client found.");
            Console.Error.WriteLine("Searched Steam libraries and mounts. Use --index or set POE2_INDEX.");
            return 3;
        }

        Console.WriteLine($"Found {candidates.Count} candidate(s):");
        foreach (var candidate in candidates)
        {
            Console.WriteLine();
            Console.WriteLine($"  game   : {candidate.GameRoot}");
            Console.WriteLine($"  index  : {candidate.IndexPath}");
            Console.WriteLine($"  exists : {candidate.IsSteamBundles}");
            Console.WriteLine($"  mtime  : {candidate.GameVersionHint}");
        }

        return 0;
    }
}