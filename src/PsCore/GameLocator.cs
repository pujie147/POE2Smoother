namespace PsCore;

/// <summary>
/// App-level game discovery. Every supported client layout ends up in one candidate list:
/// the Steam bundle index first, then standalone Content.ggpk installs.
/// </summary>
public static class GameLocator
{
    public static IReadOnlyList<GameLocation> FindCandidates()
    {
        var steam = SteamLibraryLocator.FindCandidates();
        var merged = new List<GameLocation>(steam);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in steam)
            seen.Add(Path.GetFullPath(location.TargetPath));

        foreach (var standalone in StandaloneLocator.FindCandidates(steam.Select(s => s.GameRoot)))
        {
            if (seen.Add(Path.GetFullPath(standalone.TargetPath)))
                merged.Add(standalone);
        }

        return merged;
    }

    public static GameLocation? FindBest()
    {
        var candidates = FindCandidates();
        return candidates.FirstOrDefault(l => l.IsSteamBundles) ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// Resolves a path the user picked by hand: a game folder, Bundles2/_.index.bin or Content.ggpk.
    /// </summary>
    public static GameLocation FromManualPath(string path)
    {
        var full = Path.GetFullPath(path);

        if (Directory.Exists(full))
        {
            var steamRoot = SteamLibraryLocator.FromGameRoot(full);
            if (File.Exists(steamRoot.IndexPath))
                return steamRoot;

            var standaloneRoot = StandaloneLocator.FromGameRoot(full);
            if (standaloneRoot != null)
                return standaloneRoot;

            throw new InvalidOperationException(
                $"no Bundles2/_.index.bin or {StandaloneLocator.GgpkFileName} under {full}");
        }

        if (!File.Exists(full))
            throw new FileNotFoundException("File not found", full);

        if (string.Equals(Path.GetFileName(full), StandaloneLocator.GgpkFileName, StringComparison.OrdinalIgnoreCase))
            return StandaloneLocator.FromGgpk(full);

        var location = SteamLibraryLocator.FromIndex(full);
        if (!File.Exists(location.IndexPath))
            throw new FileNotFoundException("Not a usable _.index.bin", location.IndexPath);
        return location;
    }
}