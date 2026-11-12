using LibGGPK3;
using LibGGPK3.Records;
using GgpkFileRecord = LibGGPK3.Records.FileRecord;

namespace PsCore;

/// <summary>
/// Per-target file operations. Steam writes a loose <c>Bundles2/_.index.bin</c> (cheap work copy,
/// atomic rename); the standalone client keeps that index inside Content.ggpk, so writes are
/// in-place and every entry point here opens and closes the container itself. No session may be
/// open while <see cref="RestoreBackup"/> runs, otherwise the session's buffered index is flushed
/// back over the restored bytes.
/// </summary>
public abstract class TargetStore
{
    protected TargetStore(GameLocation game) => Game = game;

    public GameLocation Game { get; }
    public string TargetPath => Game.TargetPath;
    public bool IsGgpk => Game.IsGgpk;

    public abstract bool Exists();
    public abstract long IndexSize();
    public abstract string HashIndex();
    public abstract string CreateBackup(string backupsDir);
    public abstract void RestoreBackup(string backupPath);
    public abstract bool HasCustomBundles();
    public abstract IReadOnlyList<string> ListCustomBundles();
    public abstract void DeleteCustomBundles();

    /// <summary>
    /// Standalone only: refuses to start a write when a previous one never finished (journal file)
    /// and when the container drive lacks room. Steam writes are atomic and need neither check.
    /// </summary>
    public abstract void EnsureWritable(long estimatedBytes, bool force);

    public abstract void ClearWriteMarker();

    public static TargetStore For(GameLocation game)
        => game.IsGgpk ? new GgpkTargetStore(game) : new SteamTargetStore(game);
}

internal sealed class SteamTargetStore : TargetStore
{
    public SteamTargetStore(GameLocation game) : base(game)
    {
    }

    public override bool Exists() => File.Exists(Game.IndexPath);
    public override long IndexSize() => Exists() ? new FileInfo(Game.IndexPath).Length : 0;
    public override string HashIndex() => HashUtil.Sha256File(Game.IndexPath);
    public override string CreateBackup(string backupsDir) => BackupManager.CreateBackup(Game.IndexPath, backupsDir);
    public override void RestoreBackup(string backupPath) => BackupManager.RestoreBackup(backupPath, Game.IndexPath);
    public override bool HasCustomBundles() => BackupManager.HasCustomBundles(Game.BundlesDir);
    public override IReadOnlyList<string> ListCustomBundles() => BackupManager.ListCustomBundleFiles(Game.BundlesDir);
    public override void DeleteCustomBundles() => BackupManager.DeleteCustomBundles(Game.BundlesDir);
    public override void EnsureWritable(long estimatedBytes, bool force)
    {
    }

    public override void ClearWriteMarker()
    {
    }
}

internal sealed class GgpkTargetStore : TargetStore
{
    private const string CustomBundleDirName = "LibGGPK3";
    private const string IndexEntryName = "_.index.bin";
    private const string CustomBundlesDirRelative = "Bundles2/" + CustomBundleDirName;

    private readonly string _ggpkPath;

    public GgpkTargetStore(GameLocation game) : base(game)
    {
        _ggpkPath = game.GgpkPath
                    ?? throw new ArgumentException("GameLocation has no Content.ggpk path", nameof(game));
    }

    private string JournalPath => _ggpkPath + ".psjournal";

    public override bool Exists() => File.Exists(_ggpkPath);

    public override long IndexSize()
    {
        using var stream = OpenStream(writable: false);
        using var ggpk = new GGPK(stream, leaveOpen: true);
        return IndexRecord(ggpk).DataLength;
    }

    public override string HashIndex() => HashUtil.Sha256Bytes(ReadIndexBytes());

    public override string CreateBackup(string backupsDir)
    {
        var bytes = ReadIndexBytes();
        Directory.CreateDirectory(backupsDir);
        // The name must end in _.index.bin so BackupManager.Prune's *.bin sweep sees it.
        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{IndexEntryName}";
        var path = Path.Combine(backupsDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public override void RestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup not found", backupPath);

        var bytes = File.ReadAllBytes(backupPath);
        WriteIndexBytes(bytes);

        var written = ReadIndexBytes();
        if (written.LongLength != bytes.LongLength)
            throw new InvalidDataException(
                $"Index restore verification failed: expected {bytes.LongLength} bytes, read {written.LongLength}.");
    }

    public override bool HasCustomBundles() => ListCustomBundles().Count > 0;

    public override IReadOnlyList<string> ListCustomBundles()
    {
        using var stream = OpenStream(writable: false);
        using var ggpk = new GGPK(stream, leaveOpen: true);

        var results = new List<string>();
        foreach (var bundle in CustomBundles(ggpk))
            results.Add(CustomBundlesDirRelative + "/" + bundle.Name);
        return results;
    }

    public override void DeleteCustomBundles()
    {
        using var stream = OpenStream(writable: true);
        using var ggpk = new GGPK(stream, leaveOpen: true);

        var dir = CustomBundleDirectory(ggpk);
        if (dir == null)
            return;

        var removed = false;
        foreach (var node in dir.ToList())
        {
            node.Remove();
            removed = true;
        }

        if (dir.Count == 0)
            dir.Remove();

        if (removed)
            ggpk.Flush();
    }

    public override void EnsureWritable(long estimatedBytes, bool force)
    {
        if (File.Exists(JournalPath))
        {
            if (!force)
                throw new InvalidOperationException(
                    "A previous standalone write did not finish (" + JournalPath + "). " +
                    "Restore from the backup first, or pass --force to discard the marker.");

            TryDeleteJournal();
        }

        var required = IndexSize() * 2 + Math.Max(0, estimatedBytes);
        if (required > 0)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(_ggpkPath));
                if (!string.IsNullOrEmpty(root))
                {
                    var free = new DriveInfo(root).AvailableFreeSpace;
                    if (free < required)
                        throw new IOException(
                            $"Not enough free space on {root}: need {required:N0} bytes, have {free:N0}.");
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch
            {
                // drive stats can fail on unusual mounts; don't block the write over that
            }
        }

        try
        {
            File.WriteAllText(JournalPath, $"{DateTime.UtcNow:O} {_ggpkPath}");
        }
        catch
        {
            // the marker is best effort; the backup still protects the user
        }
    }

    public override void ClearWriteMarker() => TryDeleteJournal();

    private void TryDeleteJournal()
    {
        try
        {
            if (File.Exists(JournalPath))
                File.Delete(JournalPath);
        }
        catch
        {
            // ignore
        }
    }

    private FileStream OpenStream(bool writable) => new(_ggpkPath, FileMode.Open,
        writable ? FileAccess.ReadWrite : FileAccess.Read,
        FileShare.Read, 64 * 1024, FileOptions.RandomAccess);

    private byte[] ReadIndexBytes()
    {
        using var stream = OpenStream(writable: false);
        using var ggpk = new GGPK(stream, leaveOpen: true);
        return IndexRecord(ggpk).Read();
    }

    private void WriteIndexBytes(byte[] bytes)
    {
        using var stream = OpenStream(writable: true);
        using var ggpk = new GGPK(stream, leaveOpen: true);

        using (var file = new GGFileStream(IndexRecord(ggpk)))
        {
            file.SetLength(0);
            file.Write(bytes);
            file.Flush();
        }

        ggpk.Flush();
    }

    private static GgpkFileRecord IndexRecord(GGPK ggpk)
    {
        var bundles2 = Bundles2(ggpk);
        return bundles2?[IndexEntryName] as GgpkFileRecord
               ?? throw new InvalidDataException($"Bundles2/{IndexEntryName} not found in the GGPK");
    }

    private static DirectoryRecord? Bundles2(GGPK ggpk) => ggpk.Root["Bundles2"] as DirectoryRecord;

    private static DirectoryRecord? CustomBundleDirectory(GGPK ggpk)
        => Bundles2(ggpk)?[CustomBundleDirName] as DirectoryRecord;

    private static IEnumerable<GgpkFileRecord> CustomBundles(GGPK ggpk)
    {
        var dir = CustomBundleDirectory(ggpk);
        if (dir == null)
            yield break;

        foreach (var node in dir)
        {
            if (node is GgpkFileRecord file)
                yield return file;
        }
    }
}