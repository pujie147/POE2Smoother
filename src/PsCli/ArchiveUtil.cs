using System.Security.Cryptography;
using LibBundle3;
using LibBundle3.Records;
using Index = LibBundle3.Index;

namespace PsCli;

internal static class ArchiveUtil
{
    public static string NormalizeIndexPath(string value) => Path.GetFullPath(value);

    public static string ResolveBundlesDir(ParsedArgs args, string indexPath)
    {
        var bundles = args.Get("bundles");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(bundles)
            ? Path.GetDirectoryName(indexPath)!
            : bundles);
    }

    public static Index OpenReadOnly(string indexPath, string bundlesDir, out int parseFailures)
    {
        var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var index = new Index(stream, leaveOpen: false, parsePaths: false,
                new DriveBundleFactory(bundlesDir));
            parseFailures = index.ParsePaths();
            return index;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static Index OpenNoPaths(string indexPath, string bundlesDir)
    {
        var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new Index(stream, leaveOpen: false, parsePaths: false,
                bundleFactory: new DriveBundleFactory(bundlesDir));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static void EnsureOodle()
    {
        // Touching Oodle here gives a clear error before we start touching game files.
        Oodle.Initialize(new Oodle.Settings());
        Oodle.Release();
    }

    public static IEnumerable<FileRecord> ParsedFiles(Index index)
        => index.Files.Values.Where(f => !string.IsNullOrEmpty(f.Path));

    public static IEnumerable<FileRecord> Effects(Index index)
        => ParsedFiles(index).Where(f =>
            f.Path!.StartsWith("metadata/effects/", StringComparison.OrdinalIgnoreCase));

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Sha256Bytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    public static void AtomicCopy(string source, string destination)
    {
        var tmp = destination + ".tmp-" + Environment.ProcessId;
        File.Copy(source, tmp, overwrite: true);
        File.Move(tmp, destination, overwrite: true);
    }
}