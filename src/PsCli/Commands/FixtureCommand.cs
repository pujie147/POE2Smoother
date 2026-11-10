using System.Reflection;
using System.Text;
using LibBundle3;
using LibBundle3.Records;
using Index = LibBundle3.Index;

namespace PsCli.Commands;

/// <summary>
/// Builds a tiny synthetic Bundles2 directory so M0 can be tested without the game.
/// This is a development helper, not part of the product UI.
/// </summary>
internal static class FixtureCommand
{
    private const ulong EmptyMurmurHash = 17594038612473627390UL;

    public static int Run(ParsedArgs args)
    {
        var outDir = Path.GetFullPath(args.Require("out"));
        var gamePath = args.Get("path") ?? "metadata/effects/synthetic/test.ao";
        var content = Encoding.UTF8.GetBytes(args.Get("content") ?? "synthetic-effect");

        if (!gamePath.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) &&
            !gamePath.EndsWith(".pet", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("error: --path should end with .ao or .pet for the M0 target.");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        var indexPath = Path.Combine(outDir, "_.index.bin");
        if (File.Exists(indexPath))
        {
            Console.Error.WriteLine($"error: {indexPath} already exists.");
            return 3;
        }

        ArchiveUtil.EnsureOodle();

        var contentBundleRel = "Data/9";
        var contentBundlePath = Path.Combine(outDir, contentBundleRel + ".bundle.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(contentBundlePath)!);
        WriteBundle(contentBundlePath, content);

        var directoryRaw = BuildDirectoryRaw(gamePath);
        var directoryBundle = BuildBundleBytes(directoryRaw);

        var pathHash = MurmurHash64A(Encoding.UTF8.GetBytes(gamePath));
        var indexData = BuildIndexData(contentBundleRel, pathHash, content.Length,
            directoryRaw.Length, directoryBundle);

        WriteBundle(indexPath, indexData);

        Console.WriteLine($"fixture index : {indexPath}");
        Console.WriteLine($"target path   : {gamePath}");
        Console.WriteLine($"content bytes : {content.Length}");
        Console.WriteLine($"content hash  : {ArchiveUtil.Sha256Bytes(content)}");
        Console.WriteLine();
        Console.WriteLine("Test with:");
        Console.WriteLine($"  pscli inspect --index \"{indexPath}\"");
        Console.WriteLine($"  pscli self-test --index \"{indexPath}\" --yes");
        return 0;
    }

    private static byte[] BuildDirectoryRaw(string gamePath)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(1); // one path entry
        writer.Write(Encoding.UTF8.GetBytes(gamePath));
        writer.Write((byte)0);
        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildIndexData(string contentBundleRel, ulong pathHash, int contentLength,
        int directoryRawLength, byte[] directoryBundle)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(1); // bundle count
        var bundleName = Encoding.UTF8.GetBytes(contentBundleRel);
        writer.Write(bundleName.Length);
        writer.Write(bundleName);
        writer.Write(contentLength); // BundleRecord.UncompressedSize

        writer.Write(1); // file count
        writer.Write(pathHash);
        writer.Write(0); // bundle index
        writer.Write(0); // offset
        writer.Write(contentLength); // size

        writer.Write(1); // directory record count
        writer.Write(EmptyMurmurHash); // tells Index.NameHash to use MurmurHash64A
        writer.Write(0); // offset
        writer.Write(directoryRawLength); // size in uncompressed directory data
        writer.Write(directoryRawLength); // recursive size
        writer.Write(directoryBundle);

        writer.Flush();
        return ms.ToArray();
    }

    private static void WriteBundle(string path, byte[] data)
    {
        using var stream = File.Create(path);
        var bundle = CreateWriter(stream);
        bundle.Save(data, Oodle.Compressor.Mermaid, Oodle.CompressionLevel.Normal);
        bundle.Dispose();
    }

    private static byte[] BuildBundleBytes(byte[] data)
    {
        using var stream = new MemoryStream();
        var bundle = CreateWriter(stream);
        bundle.Save(data, Oodle.Compressor.Mermaid, Oodle.CompressionLevel.Normal);
        var bytes = stream.ToArray();
        bundle.Dispose();
        return bytes;
    }

    private static Bundle CreateWriter(Stream stream)
    {
        var ctor = typeof(Bundle).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Stream), typeof(BundleRecord)],
            modifiers: null);
        if (ctor == null)
            throw new MissingMethodException("LibBundle3.Bundle(Stream, BundleRecord) not found.");

        return (Bundle)ctor.Invoke([stream, null]);
    }

    private static ulong MurmurHash64A(ReadOnlySpan<byte> data, ulong seed = 322417471UL)
    {
        if (data.IsEmpty)
            return EmptyMurmurHash;

        if (data[^1] == (byte)'/')
            data = data[..^1];

        seed ^= (ulong)(data.Length * -4132994306676758123L);

        var i = 0;
        for (; i + 8 <= data.Length; i += 8)
        {
            var k = BitConverter.ToUInt64(data.Slice(i, 8));
            k *= 14313749767032793493UL;
            seed = (seed ^ ((k ^ (k >> 47)) * unchecked((ulong)-4132994306676758123L))) * 14313749767032793493UL;
        }

        var remaining = data.Length - i;
        if (remaining != 0)
        {
            ulong k = 0;
            for (var j = 0; j < remaining; j++)
                k |= (ulong)data[i + j] << (8 * j);
            seed = (seed ^ k) * 14313749767032793493UL;
        }

        seed = (seed ^ (seed >> 47)) * 14313749767032793493UL;
        return seed ^ (seed >> 47);
    }
}