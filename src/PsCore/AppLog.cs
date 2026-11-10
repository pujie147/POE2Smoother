namespace PsCore;

public static class AppLog
{
    private static readonly object Gate = new();

    public static string PathFor(AppPaths paths) => Path.Combine(paths.LogsDir, "app.log");

    public static void Info(AppPaths paths, string message) => Write(paths, "INFO", message);

    public static void Warn(AppPaths paths, string message) => Write(paths, "WARN", message);

    public static void Error(AppPaths paths, string message) => Write(paths, "ERROR", message);

    public static void Write(AppPaths paths, string level, string message)
    {
        try
        {
            paths.Ensure();
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(PathFor(paths), line);
            }
        }
        catch
        {
            // logging must never break the operation
        }
    }

    public static string[] ReadTail(AppPaths paths, int lines)
    {
        var path = PathFor(paths);
        if (!File.Exists(path))
            return [];

        try
        {
            return File.ReadLines(path).TakeLast(Math.Max(1, lines)).ToArray();
        }
        catch
        {
            return [];
        }
    }
}