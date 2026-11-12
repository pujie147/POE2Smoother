namespace PsCore;

public enum GameLocationKind
{
    SteamBundles,
    StandaloneGgpk
}

public sealed record GameLocation(
    string GameRoot,
    string BundlesDir,
    string IndexPath,
    string? LibraryRoot = null,
    GameLocationKind Kind = GameLocationKind.SteamBundles,
    string? GgpkPath = null)
{
    public bool IsSteamBundles => Kind == GameLocationKind.SteamBundles;
    public bool IsGgpk => Kind == GameLocationKind.StandaloneGgpk;

    /// <summary>File that actually gets patched: the bundle index or Content.ggpk.</summary>
    public string TargetPath => IsGgpk ? GgpkPath ?? "" : IndexPath;

    public string KindLabel => IsGgpk ? "standalone (Content.ggpk)" : "steam";

    public string DisplayName => GameRoot;

    public string GameVersionHint =>
        File.Exists(TargetPath)
            ? File.GetLastWriteTimeUtc(TargetPath).ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "missing";
}