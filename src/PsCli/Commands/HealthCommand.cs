using System.Text.RegularExpressions;
using PsCore;
using PsRules;

namespace PsCli.Commands;

/// <summary>
/// Crosses the client's own log with the files this tool wrote, so "did blanking break loading?" is
/// answered by the client rather than guessed at. Read-only: it never restores, never writes to the
/// game, and treats a clean log as weak evidence (see the note it prints).
/// </summary>
internal static class HealthCommand
{
    // Only these extensions are ours to blame for; the alternation in QuotedPath is the same list.

    private static readonly Regex QuotedPath = new(
        @"""([^""\r\n]+?" + @"(?:\.ao|\.dds|\.pet|\.epk|\.mat|\.fxgraph|\.aoc))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Severity = new(
        @"\[(?<level>CRIT|WARN|ERROR)[^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static int Run(ParsedArgs args)
    {
        var paths = CommandUtil.Paths();
        var game = CommandUtil.ResolveGame(args);
        var service = new AppService(paths);
        var status = service.GetStatus(game);

        if (status.State is not { PatchedFiles.Count: > 0 } state)
        {
            Console.WriteLine("no applied state for this client — nothing to check / 没有已应用记录");
            return 0;
        }

        var logPath = ResolveLog(args.Get("log"), game);
        if (logPath == null || !File.Exists(logPath))
        {
            Console.Error.WriteLine(
                "error: client log not found. Looked in <game>/logs/LatestClient.txt; pass --log <file> " +
                "if the client keeps it elsewhere. / 找不到客户端日志,可用 --log 指定");
            return 2;
        }

        // The index maps paths lowercase; the client prints them capitalised, and both may carry a
        // container-relative prefix, so compare normalised suffixes rather than raw strings.
        var written = new Dictionary<string, PatchedFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in state.PatchedFiles)
        {
            var key = Normalize(entry.Path);
            if (!string.IsNullOrEmpty(key))
                written[key] = entry;
        }

        var lines = File.ReadLines(logPath).ToList();
        var hits = new List<(string Level, string Path, string Line)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            foreach (Match match in QuotedPath.Matches(line))
            {
                var key = Normalize(match.Groups[1].Value);
                if (key.Length == 0 || !written.TryGetValue(key, out var entry))
                    continue;

                if (!seen.Add(key))
                    continue;

                hits.Add((Severity.Match(line) is { Success: true } sev
                    ? sev.Groups["level"].Value.ToUpperInvariant()
                    : "LOG", entry.Path, line.Trim()));
            }
        }

        var info = new FileInfo(logPath);
        if (args.HasFlag("json"))
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                log = logPath,
                logWritten = info.LastWriteTimeUtc,
                lines = lines.Count,
                applied = state.PatchedFiles.Count,
                suspect = hits.Select(h => new { h.Level, h.Path }).ToArray()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"log        : {logPath}");
            Console.WriteLine($"log lines  : {lines.Count} (written {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm} UTC)");
            Console.WriteLine($"applied    : {state.PatchedFiles.Count} files from {state.AppliedAt:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"suspect    : {hits.Count}");
            foreach (var (level, path, line) in hits.Take(25))
            {
                Console.WriteLine($"  [{level}] {path}");
                Console.WriteLine($"           {Truncate(line, 160)}");
            }
            if (hits.Count > 25)
                Console.WriteLine($"  … {hits.Count - 25} more");
        }

        if (hits.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No client complaint names a file this tool wrote. That is weak evidence, not proof:");
            Console.WriteLine("the client logs asset-path complaints sparingly, so a clean log does not show that");
            Console.WriteLine("blanked resources load fine — it only shows no recorded complaint about them.");
            Console.WriteLine("客户端日志没有点到本次写入的文件;这只说明「没记录到抱怨」,不等于「一定正常」。");
            return 0;
        }

        if (!args.HasFlag("json"))
        {
            Console.WriteLine();
            Console.WriteLine("Undo just these files' tier without losing the rest of the suppression / 只撤销该档位:");
            Console.WriteLine($"  pscli restore --undo-tiers {(hits.Any(h => IsMaterial(h.Path)) ? "material" : "fx")} …");
        }

        return 7;
    }

    private static bool IsMaterial(string path) =>
        path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('"').TrimEnd('"');

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    /// <summary>
    /// The client writes this next to the game on both platforms; the older Bundles2 location is
    /// probed too, because a client build has been seen to log there.
    /// </summary>
    private static string? ResolveLog(string? @explicit, GameLocation game)
    {
        if (!string.IsNullOrWhiteSpace(@explicit))
            return Path.GetFullPath(@explicit);

        foreach (var candidate in new[]
        {
            Path.Combine(game.GameRoot, "logs", "LatestClient.txt"),
            Path.Combine(game.GameRoot, "logs", "Client.txt"),
            Path.Combine(game.BundlesDir, "logs", "LatestClient.txt")
        })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
