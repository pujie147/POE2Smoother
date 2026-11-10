using System.Text.RegularExpressions;

namespace PsCore;

public static class SteamLibraryLocator
{
    public const string Poe2AppId = "2694490";
    public const string DefaultGameFolder = "Path of Exile 2";

    private static readonly Regex PathRegex = new(
        "\"path\"\\s+\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InstallDirRegex = new(
        "\"installdir\"\\s+\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<GameLocation> FindCandidates()
    {
        var results = new List<GameLocation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamAppsRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var directIndex = Environment.GetEnvironmentVariable("POE2_INDEX");
        if (!string.IsNullOrWhiteSpace(directIndex))
            AddLocation(results, seen, FromIndex(directIndex));

        var directRoot = Environment.GetEnvironmentVariable("POE2_ROOT");
        if (!string.IsNullOrWhiteSpace(directRoot))
            AddLocation(results, seen, FromGameRoot(directRoot));

        foreach (var root in SteamRoots())
            AddSteamRoot(steamAppsRoots, root);

        foreach (var mount in MountRoots())
        {
            foreach (var steamApps in EnumerateMountSteamApps(mount))
                steamAppsRoots.Add(steamApps);
        }

        foreach (var steamApps in steamAppsRoots)
        {
            foreach (var folder in CandidateFolderNames(steamApps))
            {
                var gameRoot = Path.Combine(steamApps, "common", folder);
                AddLocation(results, seen, FromGameRoot(gameRoot, steamApps));
            }
        }

        return results
            .OrderByDescending(l => l.IsSteamBundles)
            .ThenBy(l => l.IndexPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GameLocation? FindBest() => FindCandidates().FirstOrDefault(l => l.IsSteamBundles);

    public static GameLocation FromIndex(string indexPath)
    {
        var fullIndex = Path.GetFullPath(indexPath);
        var bundlesDir = Path.GetDirectoryName(fullIndex)
                         ?? throw new InvalidOperationException($"Cannot get Bundles2 directory from {indexPath}");
        var gameRoot = Path.GetDirectoryName(bundlesDir)
                       ?? throw new InvalidOperationException($"Cannot get game root from {indexPath}");
        return new GameLocation(gameRoot, bundlesDir, fullIndex, null);
    }

    public static GameLocation FromGameRoot(string gameRoot, string? libraryRoot = null)
    {
        var fullRoot = Path.GetFullPath(gameRoot);
        var bundles = Path.Combine(fullRoot, "Bundles2");
        return new GameLocation(fullRoot, bundles, Path.Combine(bundles, "_.index.bin"), libraryRoot);
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddRoot(roots, Environment.GetEnvironmentVariable("STEAM_ROOT"));
        AddRoot(roots, Path.Combine(home, ".local", "share", "Steam"));
        AddRoot(roots, Path.Combine(home, ".steam", "steam"));
        AddRoot(roots, Path.Combine(home, ".steam", "root"));
        AddRoot(roots, Path.Combine(home, "Steam"));

        return roots;
    }

    private static void AddRoot(ISet<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;
        try
        {
            if (Directory.Exists(root))
                roots.Add(Path.GetFullPath(root));
        }
        catch
        {
            // ignore malformed environment values
        }
    }

    private static void AddSteamRoot(ISet<string> steamAppsRoots, string root)
    {
        var steamApps = Path.Combine(root, "steamapps");
        if (Directory.Exists(steamApps))
            steamAppsRoots.Add(Path.GetFullPath(steamApps));

        foreach (var vdf in new[]
                 {
                     Path.Combine(steamApps, "libraryfolders.vdf"),
                     Path.Combine(root, "config", "libraryfolders.vdf")
                 })
        {
            if (!File.Exists(vdf))
                continue;

            try
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in PathRegex.Matches(text))
                {
                    var path = Unescape(match.Groups[1].Value);
                    var apps = Path.Combine(path, "steamapps");
                    if (Directory.Exists(apps))
                        steamAppsRoots.Add(Path.GetFullPath(apps));
                }
            }
            catch
            {
                // a broken library file should not abort detection
            }
        }
    }

    private static IEnumerable<string> CandidateFolderNames(string steamApps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultGameFolder, "PathOfExile2" };

        try
        {
            var manifest = Path.Combine(steamApps, $"appmanifest_{Poe2AppId}.acf");
            if (File.Exists(manifest))
            {
                var match = InstallDirRegex.Match(File.ReadAllText(manifest));
                if (match.Success)
                    names.Add(Unescape(match.Groups[1].Value));
            }
        }
        catch
        {
            // ignore
        }

        return names;
    }

    private static IEnumerable<string> MountRoots()
    {
        if (OperatingSystem.IsWindows())
            yield break;

        yield return "/run/media";
        yield return "/media";
        yield return "/mnt";
    }

    private static IEnumerable<string> EnumerateMountSteamApps(string mount)
    {
        foreach (var first in SafeDirectories(mount))
        {
            yield return Path.Combine(first, "steamapps");
            yield return Path.Combine(first, "SteamLibrary", "steamapps");

            foreach (var second in SafeDirectories(first))
            {
                yield return Path.Combine(second, "steamapps");
                yield return Path.Combine(second, "SteamLibrary", "steamapps");
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static void AddLocation(List<GameLocation> results, ISet<string> seen, GameLocation location)
    {
        if (!File.Exists(location.IndexPath))
            return;
        if (seen.Add(Path.GetFullPath(location.IndexPath)))
            results.Add(location);
    }

    private static string Unescape(string value)
        => value.Replace("\\\\", "\\").Replace("\\/", "/");
}