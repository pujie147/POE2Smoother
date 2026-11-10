using System.Diagnostics;
using LibBundle3;
using LibBundle3.Records;
using Index = LibBundle3.Index;

namespace PsCli.Commands;

internal static class SelfTestCommand
{
    public static int Run(ParsedArgs args)
    {
        var indexPath = ArchiveUtil.NormalizeIndexPath(args.Require("index"));
        if (!File.Exists(indexPath))
        {
            Console.Error.WriteLine($"error: index not found: {indexPath}");
            return 2;
        }

        var bundlesDir = ArchiveUtil.ResolveBundlesDir(args, indexPath);
        var customDir = Path.Combine(bundlesDir, "LibGGPK3");
        var backupDir = args.Get("backup-dir") ?? DefaultBackupDir();
        var yes = args.HasFlag("yes");
        var keep = args.HasFlag("keep");
        var force = args.HasFlag("force");

        ArchiveUtil.EnsureOodle();

        var running = FindRunningGame();
        if (running.Count > 0)
        {
            Console.Error.WriteLine($"error: game process appears to be running: {string.Join(", ", running)}");
            if (!force)
            {
                Console.Error.WriteLine("       close the game first, or pass --force if you know it is safe.");
                return 3;
            }
            Console.Error.WriteLine("       --force: continuing anyway.");
        }

        var customExisted = Directory.Exists(customDir) &&
                            Directory.EnumerateFileSystemEntries(customDir).Any();
        if (customExisted && !force)
        {
            Console.Error.WriteLine($"error: custom bundles already exist: {customDir}");
            Console.Error.WriteLine("       restore/clean them first, or pass --force to move them aside.");
            return 3;
        }

        string? movedCustomDir = null;
        if (customExisted)
        {
            movedCustomDir = customDir + ".m0backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            Directory.Move(customDir, movedCustomDir);
            Console.WriteLine($"moved existing custom bundles to: {movedCustomDir}");
        }

        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Path.GetFileName(indexPath)}");
        File.Copy(indexPath, backupPath, overwrite: false);
        var originalHash = ArchiveUtil.Sha256File(indexPath);

        Console.WriteLine($"index       : {indexPath}");
        Console.WriteLine($"bundles     : {bundlesDir}");
        Console.WriteLine($"backup      : {backupPath}");
        Console.WriteLine($"sha256      : {originalHash}");
        Console.WriteLine();

        if (!yes)
        {
            Console.WriteLine("M0 self-test will now:");
            Console.WriteLine("  1. replace ONE effect file with bytes identical to the original");
            Console.WriteLine("  2. write it to Bundles2/LibGGPK3/ and save _.index.bin");
            Console.WriteLine("  3. read it back and verify content/path count");
            Console.WriteLine("  4. restore _.index.bin and remove the custom bundle");
            Console.WriteLine();
            Console.WriteLine("Re-run with --yes to perform the test.");
            return 0;
        }

        var originalFileCount = -1;
        string? targetPath = null;
        string? targetHash = null;
        var testSucceeded = false;
        var exitCode = 0;

        try
        {
            using (var index = new Index(indexPath, parsePaths: false,
                       new DriveBundleFactory(bundlesDir)))
            {
                var pathFailures = index.ParsePaths();
                if (pathFailures != 0)
                    Console.WriteLine($"note        : {pathFailures} files had no parseable path (tolerated)");

                originalFileCount = index.Files.Count;
                var target = SelectTarget(index, args.Get("game-path"));
                if (target == null)
                {
                    Console.Error.WriteLine("error: no suitable test file found.");
                    return 5;
                }

                targetPath = target.Path!;
                var originalBytes = target.Read().ToArray();
                targetHash = ArchiveUtil.Sha256Bytes(originalBytes);

                Console.WriteLine($"target      : {targetPath}");
                Console.WriteLine($"size        : {ArchiveUtil.FormatBytes(originalBytes.Length)}");
                Console.WriteLine($"content hash: {targetHash}");
                Console.WriteLine("writing identical bytes...");

                target.Write(originalBytes);
                index.Save();
            }

            Console.WriteLine("index saved.");
            PrintCustomBundles(customDir);

            using (var verify = ArchiveUtil.OpenReadOnly(indexPath, bundlesDir, out var parseFailures))
            {
                if (parseFailures != 0)
                    Console.WriteLine($"note        : {parseFailures} files had no parseable path after write (tolerated)");

                if (verify.Files.Count != originalFileCount)
                    throw new InvalidDataException(
                        $"file count changed: {originalFileCount} -> {verify.Files.Count}");

                if (!verify.TryGetFile(targetPath, out var record))
                    throw new InvalidDataException($"target disappeared from index: {targetPath}");

                var after = record.Read().ToArray();
                var afterHash = ArchiveUtil.Sha256Bytes(after);
                if (!string.Equals(afterHash, targetHash, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"content mismatch after write: {targetHash} -> {afterHash}");

                Console.WriteLine($"verified    : content hash still {afterHash}");
                Console.WriteLine($"verified    : file count still {verify.Files.Count}");
            }

            testSucceeded = true;

            if (keep)
            {
                Console.WriteLine();
                Console.WriteLine("KEPT MODIFIED INDEX (--keep).");
                Console.WriteLine("Launch PoE2 now and confirm it reaches the login screen without errors.");
                Console.WriteLine("Restore with:");
                Console.WriteLine($"  pscli restore --index \"{indexPath}\" --backup \"{backupPath}\" --clean-custom --yes");
                if (movedCustomDir != null)
                    Console.WriteLine($"Previous custom bundles were moved to: {movedCustomDir}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            if (!keep || !testSucceeded)
            {
                try
                {
                    Restore(indexPath, backupPath, customDir, movedCustomDir);
                    var restoredHash = ArchiveUtil.Sha256File(indexPath);
                    if (!string.Equals(restoredHash, originalHash, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine("error: restore hash mismatch!");
                        Console.Error.WriteLine($"       expected {originalHash}");
                        Console.Error.WriteLine($"       actual   {restoredHash}");
                        exitCode = 2;
                    }
                    else
                    {
                        Console.WriteLine($"restored    : {restoredHash}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: restore failed: {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine($"       backup kept at: {backupPath}");
                    exitCode = 2;
                }
            }
        }

        if (exitCode == 0)
        {
            Console.WriteLine();
            Console.WriteLine("M0 PASS: write + save + verify + restore completed.");
        }

        return exitCode;
    }

    private static void Restore(string indexPath, string backupPath, string customDir,
        string? movedCustomDir)
    {
        if (Directory.Exists(customDir))
            Directory.Delete(customDir, recursive: true);

        if (movedCustomDir != null && Directory.Exists(movedCustomDir))
            Directory.Move(movedCustomDir, customDir);

        ArchiveUtil.AtomicCopy(backupPath, indexPath);
    }

    private static FileRecord? SelectTarget(Index index, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!index.TryGetFile(requested, out var record))
                throw new InvalidOperationException($"file not found in index: {requested}");
            return record;
        }

        static bool Small(FileRecord f) => !string.IsNullOrEmpty(f.Path) && f.Size > 0 && f.Size <= 1_048_576;

        var target = index.Files.Values
            .Where(f => Small(f) &&
                        f.Path!.StartsWith("metadata/effects/", StringComparison.OrdinalIgnoreCase) &&
                        (f.Path.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) ||
                         f.Path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Size)
            .FirstOrDefault();

        target ??= index.Files.Values
            .Where(f => Small(f) &&
                        f.Path!.StartsWith("metadata/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Size)
            .FirstOrDefault();

        return target;
    }

    private static void PrintCustomBundles(string customDir)
    {
        if (!Directory.Exists(customDir))
        {
            Console.WriteLine("custom dir  : (not created)");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(customDir, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            Console.WriteLine($"custom      : {file} ({ArchiveUtil.FormatBytes(info.Length)})");
        }
    }

    private static List<string> FindRunningGame()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p => p.ProcessName)
                .Where(n => n.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string DefaultBackupDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        return Path.Combine(baseDir, "poe2-smoother", "backups");
    }
}