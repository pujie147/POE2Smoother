using System.Security.Cryptography;
using System.Text;
using LibGGPK3.Records;

namespace PsTests;

/// <summary>
/// Wraps a synthetic Bundles2 directory into a minimal but valid Content.ggpk (GGPK v3), so the
/// standalone-client code path can be exercised without a real 40 GB game file.
/// Record layout follows LibGGPK3: length + tag, then the per-record fields.
/// </summary>
internal static class TestGgpkBuilder
{
    private const uint GgpkTag = 0x4B504747; // "GGPK"
    private const uint DirectoryTag = 0x52494450; // "PDIR"
    private const uint FileTag = 0x454C4946; // "FILE"
    private const uint Version = 3;

    public static string Wrap(string bundlesDir, string outGgpkPath)
    {
        var root = new Node { Name = "", IsDirectory = true };
        root.Children.Add(BuildDirectory("Bundles2", bundlesDir));

        AssignOffsets(root, 28); // right after the GGPK header record

        using var stream = File.Create(outGgpkPath);
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write(28u);
        writer.Write(GgpkTag);
        writer.Write(Version);
        writer.Write(root.Offset);
        writer.Write(0L); // first free record: none

        WriteNode(writer, root);
        writer.Flush();
        return outGgpkPath;
    }

    private static Node BuildDirectory(string name, string path)
    {
        var node = new Node { Name = name, IsDirectory = true };

        foreach (var file in Directory.GetFiles(path).OrderBy(f => f, StringComparer.Ordinal))
            node.Children.Add(new Node { Name = Path.GetFileName(file), Data = File.ReadAllBytes(file) });

        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d, StringComparer.Ordinal))
            node.Children.Add(BuildDirectory(Path.GetFileName(dir), dir));

        return node;
    }

    private static void AssignOffsets(Node node, long offset)
    {
        node.Offset = offset;
        var next = offset + RecordLength(node);
        foreach (var child in node.Children)
        {
            AssignOffsets(child, next);
            next += RecordLength(child);
        }
    }

    private static long RecordLength(Node node) => node.IsDirectory
        ? 8 + 4 + 4 + 32 + 2L * (node.Name.Length + 1) + 12L * node.Children.Count
        : 8 + 4 + 32 + 2L * (node.Name.Length + 1) + node.Data.Length;

    private static void WriteNode(BinaryWriter writer, Node node)
    {
        if (writer.BaseStream.Position != node.Offset)
            throw new InvalidOperationException(
                $"record layout drift: {node.Name} at {writer.BaseStream.Position}, expected {node.Offset}");

        if (node.IsDirectory)
        {
            WriteDirectory(writer, node);
            foreach (var child in node.Children)
                WriteNode(writer, child);
        }
        else
        {
            WriteFile(writer, node);
        }
    }

    private static void WriteDirectory(BinaryWriter writer, Node node)
    {
        var entries = node.Children
            .Select(c => (Hash: TreeNode.GetNameHash(c.Name), c.Offset))
            .OrderBy(e => e.Hash)
            .ToList();

        writer.Write((uint)RecordLength(node));
        writer.Write(DirectoryTag);
        writer.Write(node.Name.Length + 1);
        writer.Write(entries.Count);
        writer.Write(DirectoryHash(node));
        WriteName(writer, node.Name);

        foreach (var (hash, offset) in entries)
        {
            writer.Write(hash);
            writer.Write(offset);
        }
    }

    private static void WriteFile(BinaryWriter writer, Node node)
    {
        writer.Write((uint)RecordLength(node));
        writer.Write(FileTag);
        writer.Write(node.Name.Length + 1);
        writer.Write(SHA256.HashData(node.Data));
        WriteName(writer, node.Name);
        writer.Write(node.Data);
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        foreach (var ch in name)
            writer.Write((ushort)ch);
        writer.Write((ushort)0);
    }

    /// <summary>SHA-256 over the concatenated child hashes, in entry (name-hash) order.</summary>
    private static byte[] DirectoryHash(Node node)
    {
        var ordered = node.Children
            .OrderBy(c => TreeNode.GetNameHash(c.Name))
            .Select(c => c.IsDirectory ? DirectoryHash(c) : SHA256.HashData(c.Data))
            .ToList();

        return ordered.Count == 0 ? new byte[32] : SHA256.HashData(ordered.SelectMany(h => h).ToArray());
    }

    private sealed class Node
    {
        public string Name = "";
        public bool IsDirectory;
        public byte[] Data = [];
        public List<Node> Children = [];
        public long Offset;
    }
}