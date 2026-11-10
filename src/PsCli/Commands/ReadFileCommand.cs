using PsCore;

namespace PsCli.Commands;

internal static class ReadFileCommand
{
    public static int Run(ParsedArgs args)
    {
        var indexPath = Path.GetFullPath(args.Require("index"));
        var bundlesDir = args.Get("bundles") is { Length: > 0 } b
            ? Path.GetFullPath(b)
            : Path.GetDirectoryName(indexPath)!;
        var gamePath = args.Require("path");
        var outPath = args.Get("out");

        using var session = IndexSession.OpenReadOnly(indexPath, bundlesDir);
        if (!session.TryGetFile(gamePath, out var record) || record == null)
        {
            Console.Error.WriteLine($"error: file not found in index: {gamePath}");
            return 2;
        }

        var bytes = record.Read().ToArray();
        Console.WriteLine($"path   : {gamePath}");
        Console.WriteLine($"size   : {bytes.Length}");
        Console.WriteLine($"sha256 : {HashUtil.Sha256Bytes(bytes)}");

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllBytes(outPath, bytes);
            Console.WriteLine($"written: {Path.GetFullPath(outPath)}");
        }
        else if (args.HasFlag("text"))
        {
            Console.WriteLine();
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        }

        return 0;
    }
}