using LibBundle3;
using LibBundle3.Records;
using LibBundledGGPK3;
using Index = LibBundle3.Index;

namespace PsCore;

/// <summary>
/// Open index over either client layout: a Steam <c>Bundles2/_.index.bin</c> file, or the
/// <c>Bundles2/_.index.bin</c> record inside a standalone <c>Content.ggpk</c>.
/// The patching API (<see cref="Index"/>, <see cref="TryGetFile"/>, <see cref="Save"/>) is the
/// same for both; only opening and closing differ.
/// </summary>
public sealed class IndexSession : IDisposable
{
    private bool _disposed;
    private bool _committed;
    private readonly BundledGGPK? _ggpk;
    private readonly Stream? _ggpkStream;

    public Index Index { get; }
    public GameLocation Game { get; }
    public string? WorkPath { get; }
    public int PathFailures { get; }
    public bool IsWorkCopy => WorkPath != null;
    public bool IsGgpk => Game.IsGgpk;

    private IndexSession(Index index, GameLocation game, string? workPath, int failures,
        BundledGGPK? ggpk = null, Stream? ggpkStream = null)
    {
        Index = index;
        Game = game;
        WorkPath = workPath;
        PathFailures = failures;
        _ggpk = ggpk;
        _ggpkStream = ggpkStream;
    }

    public static IndexSession OpenReadOnly(GameLocation game)
        => game.IsGgpk ? OpenGgpk(game, writable: false) : OpenSteamReadOnly(game);

    public static IndexSession OpenWorkCopy(GameLocation game)
        => game.IsGgpk ? OpenGgpk(game, writable: true) : OpenSteamWorkCopy(game);

    /// <summary>Raw-index overload for diagnostics that never touch the game folder layout.</summary>
    public static IndexSession OpenReadOnly(string indexPath, string bundlesDir)
        => OpenSteamReadOnly(SteamLocation(indexPath, bundlesDir));

    /// <inheritdoc cref="OpenReadOnly(string, string)"/>
    public static IndexSession OpenWorkCopy(string indexPath, string bundlesDir)
        => OpenSteamWorkCopy(SteamLocation(indexPath, bundlesDir));

    private static GameLocation SteamLocation(string indexPath, string bundlesDir)
    {
        var fullBundles = Path.GetFullPath(bundlesDir);
        var gameRoot = Path.GetDirectoryName(fullBundles) ?? fullBundles;
        return new GameLocation(gameRoot, fullBundles, Path.GetFullPath(indexPath));
    }

    private static IndexSession OpenSteamReadOnly(GameLocation game)
    {
        var stream = new FileStream(game.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var index = new Index(stream, leaveOpen: false, parsePaths: false,
                new DriveBundleFactory(game.BundlesDir));
            var failures = index.ParsePaths();
            return new IndexSession(index, game, null, failures);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static IndexSession OpenSteamWorkCopy(GameLocation game)
    {
        var workPath = game.IndexPath + ".pswork";

        if (File.Exists(workPath))
            File.Delete(workPath);

        File.Copy(game.IndexPath, workPath, overwrite: false);

        var stream = new FileStream(workPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var index = new Index(stream, leaveOpen: false, parsePaths: false,
                new DriveBundleFactory(game.BundlesDir));
            var failures = index.ParsePaths();
            return new IndexSession(index, game, workPath, failures);
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

    /// <summary>
    /// Standalone client: the index lives inside Content.ggpk, so there is no cheap work copy and
    /// no atomic rename — writes go straight into the container. Read-only sessions must pass a
    /// read-only stream: <c>GGPK.Dispose</c> skips its flush (and its own stream close) in that case.
    /// </summary>
    private static IndexSession OpenGgpk(GameLocation game, bool writable)
    {
        var ggpkPath = game.GgpkPath;
        if (string.IsNullOrWhiteSpace(ggpkPath))
            throw new ArgumentException("GameLocation has no Content.ggpk path", nameof(game));

        var stream = new FileStream(ggpkPath, FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.RandomAccess);

        try
        {
            var ggpk = new BundledGGPK(stream, leaveOpen: true, parsePathsInIndex: false);
            var failures = ggpk.Index.ParsePaths();
            return new IndexSession(ggpk.Index, game, null, failures, ggpk, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Save() => Index.Save();

    public void Commit()
    {
        if (IsGgpk)
        {
            _ggpk!.Flush();
            _committed = true;
            return;
        }

        if (WorkPath == null)
            throw new InvalidOperationException("Read-only session cannot be committed.");

        Index.Dispose();
        HashUtil.AtomicMove(WorkPath, Game.IndexPath);
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

        if (_ggpk != null)
        {
            try
            {
                // disposes the index, then flushes the container when it was opened writable
                _ggpk.Dispose();
            }
            catch
            {
                // ignore
            }

            try
            {
                _ggpkStream?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            try
            {
                Index.Dispose();
            }
            catch
            {
                // ignore
            }
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