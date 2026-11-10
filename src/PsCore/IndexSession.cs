using LibBundle3;
using LibBundle3.Records;
using Index = LibBundle3.Index;

namespace PsCore;

public sealed class IndexSession : IDisposable
{
    private bool _disposed;
    private bool _committed;

    public Index Index { get; }
    public string IndexPath { get; }
    public string BundlesDir { get; }
    public string? WorkPath { get; }
    public int PathFailures { get; }
    public bool IsWorkCopy => WorkPath != null;

    private IndexSession(Index index, string indexPath, string bundlesDir, string? workPath, int failures)
    {
        Index = index;
        IndexPath = indexPath;
        BundlesDir = bundlesDir;
        WorkPath = workPath;
        PathFailures = failures;
    }

    public static IndexSession OpenReadOnly(string indexPath, string bundlesDir)
    {
        var fullIndex = Path.GetFullPath(indexPath);
        var fullBundles = Path.GetFullPath(bundlesDir);
        var stream = new FileStream(fullIndex, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var index = new Index(stream, leaveOpen: false, parsePaths: false,
                new DriveBundleFactory(fullBundles));
            var failures = index.ParsePaths();
            return new IndexSession(index, fullIndex, fullBundles, null, failures);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static IndexSession OpenWorkCopy(string indexPath, string bundlesDir)
    {
        var fullIndex = Path.GetFullPath(indexPath);
        var fullBundles = Path.GetFullPath(bundlesDir);
        var workPath = fullIndex + ".pswork";

        if (File.Exists(workPath))
            File.Delete(workPath);

        File.Copy(fullIndex, workPath, overwrite: false);

        var stream = new FileStream(workPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var index = new Index(stream, leaveOpen: false, parsePaths: false,
                new DriveBundleFactory(fullBundles));
            var failures = index.ParsePaths();
            return new IndexSession(index, fullIndex, fullBundles, workPath, failures);
        }
        catch
        {
            stream.Dispose();
            try
            {
                File.Delete(workPath);
            }
            catch
            {
                // ignore
            }
            throw;
        }
    }

    public void Save() => Index.Save();

    public void Commit()
    {
        if (WorkPath == null)
            throw new InvalidOperationException("Read-only session cannot be committed.");

        Index.Dispose();
        HashUtil.AtomicMove(WorkPath, IndexPath);
        _committed = true;
    }

    public IEnumerable<FileRecord> ParsedFiles()
        => Index.Files.Values.Where(f => !string.IsNullOrEmpty(f.Path));

    public IEnumerable<FileRecord> EffectFiles()
        => ParsedFiles().Where(f =>
            f.Path!.StartsWith("metadata/effects/", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<FileRecord> RuleCandidateFiles()
        => ParsedFiles().Where(f =>
            f.Path!.StartsWith("metadata/effects/", StringComparison.OrdinalIgnoreCase) ||
            f.Path.StartsWith("metadata/terrain/leagues/", StringComparison.OrdinalIgnoreCase) ||
            f.Path.StartsWith("metadata/materials/environment/", StringComparison.OrdinalIgnoreCase));

    public bool TryGetFile(string path, out FileRecord? record)
        => Index.TryGetFile(path, out record);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            Index.Dispose();
        }
        catch
        {
            // ignore
        }

        if (WorkPath != null && !_committed && File.Exists(WorkPath))
        {
            try
            {
                File.Delete(WorkPath);
            }
            catch
            {
                // ignore
            }
        }
    }
}