namespace PsCore;

public sealed record GameLocation(
    string GameRoot,
    string BundlesDir,
    string IndexPath,
    string? LibraryRoot = null)
{
    public bool IsSteamBundles => File.Exists(IndexPath);
    public string DisplayName => GameRoot;
    public string GameVersionHint =>
        File.Exists(IndexPath)
            ? File.GetLastWriteTimeUtc(IndexPath).ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "missing";
}