using System.Reflection;
using System.Text;
using LibBundle3;
using LibBundle3.Records;
using Index = LibBundle3.Index;

namespace PsTests;

internal static class TestIndexBuilder
{
    private const ulong EmptyMurmurHash = 17594038612473627390UL;

    public static string Build(string outDir, IEnumerable<(string Path, byte[] Content)> files)
    {
        Directory.CreateDirectory(outDir);
        var indexPath = Path.Combine(outDir, "_.index.bin");
        if (File.Exists(indexPath))
            File.Delete(indexPath);

        Oodle.Initialize(new Oodle.Settings());

        var fileList = files.ToList();
        var content = new MemoryStream();
        var offsets = new List<(string Path, int Offset, int Size)>();

        foreach (var (path, bytes) in fileList)
        {
            offsets.Add((path, (int)content.Length, bytes.Length));
            content.Write(bytes);
        }

        var contentBytes = content.ToArray();
        var contentBundleRel = "Data/9";
        var contentBundlePath = Path.Combine(outDir, contentBundleRel + ".bundle.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(contentBundlePath)!);
        WriteBundle(contentBundlePath, contentBytes);

        var directoryRaw = BuildDirectoryRaw(fileList.Select(f => f.Path));
        var directoryBundle = BuildBundleBytes(directoryRaw);

        var indexData = BuildIndexData(contentBundleRel, contentBytes.Length, offsets, directoryRaw.Length,
            directoryBundle);
        WriteBundle(indexPath, indexData);
        return indexPath;
    }

    public static string NewRoot([System.Runtime.CompilerServices.CallerMemberName] string test = "test")
        => Path.Combine(Path.GetTempPath(), "poe2-smoother-tests",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{test}-{Guid.NewGuid():N}");

    private static byte[] BuildDirectoryRaw(IEnumerable<string> paths)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        foreach (var path in paths)
        {
            writer.Write(1);
            writer.Write(Encoding.UTF8.GetBytes(path));
            writer.Write((byte)0);
        }
        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildIndexData(string contentBundleRel, int contentLength,
        IReadOnlyList<(string Path, int Offset, int Size)> files, int directoryRawLength,
        byte[] directoryBundle)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(1);
        var bundleName = Encoding.UTF8.GetBytes(contentBundleRel);
        writer.Write(bundleName.Length);
        writer.Write(bundleName);
        writer.Write(contentLength);

        writer.Write(files.Count);
        foreach (var (path, offset, size) in files)
        {
            writer.Write(MurmurHash64A(Encoding.UTF8.GetBytes(path)));
            writer.Write(0);
            writer.Write(offset);
            writer.Write(size);
        }

        writer.Write(1);
        writer.Write(EmptyMurmurHash);
        writer.Write(0);
        writer.Write(directoryRawLength);
        writer.Write(directoryRawLength);
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
            seed = (seed ^ ((k ^ (k >> 47)) * unchecked((ulong)-4132994306676758123L))) *
                   14313749767032793493UL;
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