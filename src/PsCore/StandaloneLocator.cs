using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PsCore;

/// <summary>
/// Finds the standalone Windows client, which keeps everything inside one Content.ggpk.
/// </summary>
public static class StandaloneLocator
{
    public const string GgpkFileName = "Content.ggpk";

    private static readonly string[] InstallValueNames =
        ["InstallLocation", "InstallPath", "InstallDirectory", "Path"];

    public static GameLocation FromGgpk(string ggpkPath)
    {
        var full = Path.GetFullPath(ggpkPath);
        var gameRoot = Path.GetDirectoryName(full)
                       ?? throw new InvalidOperationException($"Cannot get game root from {ggpkPath}");
        return new GameLocation(gameRoot, "", "", null, GameLocationKind.StandaloneGgpk, full);
    }

    /// <summary>Returns the standalone install at <paramref name="gameRoot"/>, or null if there is none.</summary>
    public static GameLocation? FromGameRoot(string gameRoot)
    {
        var fullRoot = Path.GetFullPath(gameRoot);
        var ggpk = Path.Combine(fullRoot, GgpkFileName);
        return File.Exists(ggpk) ? FromGgpk(ggpk) : null;
    }

    /// <param name="knownGameRoots">
    /// Folders already discovered elsewhere (the Steam libraries): a standalone copy placed in a
    /// Steam library is still a Content.ggpk install.
    /// </param>
    public static IReadOnlyList<GameLocation> FindCandidates(IEnumerable<string>? knownGameRoots = null)
    {
        var results = new List<GameLocation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CandidateRoots(knownGameRoots))
        {
            GameLocation? location;
            try
            {
                location = FromGameRoot(root);
            }
            catch
            {
                continue;
            }

            if (location == null)
                continue;

            if (seen.Add(Path.GetFullPath(location.TargetPath)))
                results.Add(location);
        }

        var direct = Environment.GetEnvironmentVariable("POE2_GGPK");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            try
            {
                var location = FromGgpk(direct);
                if (File.Exists(location.TargetPath) && seen.Add(Path.GetFullPath(location.TargetPath)))
                    results.Add(location);
            }
            catch
            {
                // malformed environment value
            }
        }

        return results;
    }

    private static IEnumerable<string> CandidateRoots(IEnumerable<string>? knownGameRoots)
    {
        if (knownGameRoots != null)
        {
            foreach (var root in knownGameRoots)
                yield return root;
        }

        var envRoot = Environment.GetEnvironmentVariable("POE2_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            yield return envRoot;

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in RegistryRoots())
                yield return root;
        }

        foreach (var root in ProgramFilesRoots())
            yield return root;

        foreach (var root in DriveRoots())
            yield return root;
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            var baseDir = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(baseDir))
                continue;

            yield return Path.Combine(baseDir, "Grinding Gear Games", "Path of Exile 2");
            yield return Path.Combine(baseDir, "Path of Exile 2");
        }
    }

    private static IEnumerable<string> DriveRoots()
    {
        foreach (var drive in LocalDrives.FixedRoots())
        {
            yield return Path.Combine(drive, "Grinding Gear Games", "Path of Exile 2");
            yield return Path.Combine(drive, "Games", "Path of Exile 2");
            yield return Path.Combine(drive, "Path of Exile 2");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryRoots()
    {
        var roots = new[]
        {
            ReadRegistryRoot(Registry.CurrentUser, @"SOFTWARE\GrindingGearGames\Path of Exile 2"),
            ReadRegistryRoot(Registry.LocalMachine, @"SOFTWARE\GrindingGearGames\Path of Exile 2"),
            ReadRegistryRoot(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GrindingGearGames\Path of Exile 2"),
            ReadRegistryRoot(Registry.CurrentUser, @"SOFTWARE\GrindingGearGames\Path of Exile")
        };

        foreach (var root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root))
                yield return root;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryRoot(RegistryKey hive, string subKey)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            if (key == null)
                return null;

            foreach (var name in InstallValueNames)
            {
                if (key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
            // no access to the hive: fall through to the well-known folders
        }

        return null;
    }
}