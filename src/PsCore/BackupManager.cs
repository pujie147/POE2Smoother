namespace PsCore;

public static class BackupManager
{
    public static string CreateBackup(string indexPath, string backupsDir)
    {
        Directory.CreateDirectory(backupsDir);
        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Path.GetFileName(indexPath)}";
        var backupPath = Path.Combine(backupsDir, name);
        File.Copy(indexPath, backupPath, overwrite: false);
        return backupPath;
    }

    public static void RestoreBackup(string backupPath, string indexPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup not found", backupPath);
        HashUtil.AtomicCopy(backupPath, indexPath);
    }

    public static string CustomBundleDir(string bundlesDir) => Path.Combine(bundlesDir, "LibGGPK3");

    public static IReadOnlyList<string> ListCustomBundleFiles(string bundlesDir)
    {
        var dir = CustomBundleDir(bundlesDir);
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool HasCustomBundles(string bundlesDir)
    {
        var dir = CustomBundleDir(bundlesDir);
        return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
    }

    public static void DeleteCustomBundles(string bundlesDir)
    {
        var dir = CustomBundleDir(bundlesDir);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public static void DeleteBackup(string backupPath)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    public static void Prune(string backupsDir, int keep = 10)
    {
        try
        {
            if (!Directory.Exists(backupsDir))
                return;

            foreach (var file in Directory.EnumerateFiles(backupsDir, "*.bin")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(Math.Max(1, keep)))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // keep pruning the rest
                }
            }
        }
        catch
        {
            // pruning is best-effort
        }
    }
}