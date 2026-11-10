using System.Diagnostics;

namespace PsCore;

public static class GameProcessGuard
{
    private static readonly string[] Markers = ["PathOfExile", "PathOfExileSteam", "PathOfExile_x64"];

    public static bool IsGameRunning(out IReadOnlyList<string> processes)
    {
        var found = new List<string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                var name = process.ProcessName;
                if (Markers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    found.Add(name);
            }
        }
        catch
        {
            // /proc may be restricted; treat as unknown/not running
        }

        processes = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return processes.Count > 0;
    }
}