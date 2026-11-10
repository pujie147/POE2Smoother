using System.Security.Cryptography;

namespace PsCore;

public static class HashUtil
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Sha256Bytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    public static void AtomicCopy(string source, string destination)
    {
        var fullDestination = Path.GetFullPath(destination);
        var dir = Path.GetDirectoryName(fullDestination)
                  ?? throw new InvalidOperationException($"No directory for {destination}");
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        File.Copy(source, tmp, overwrite: true);
        File.Move(tmp, fullDestination, overwrite: true);
    }

    public static void AtomicMove(string source, string destination)
    {
        var fullDestination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        File.Move(source, fullDestination, overwrite: true);
    }
}