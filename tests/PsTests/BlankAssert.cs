using PsCore;
using PsRules;

namespace PsTests;

/// <summary>
/// Helpers for asserting on blanked assets without hardcoding template lengths in a dozen places.
/// The lengths come from production itself, so if a template changes the assertions follow it
/// instead of silently going stale — and a fixture that happens to be 4 bytes can no longer pass
/// a test that means "the template was written".
/// </summary>
public static class BlankAssert
{
    /// <summary>Longer than every template we ship, so a fixture can never look like a blanked file.</summary>
    public const int FixtureSizeFloor = 120;

    public static byte[] TemplateFor(string path) =>
        RulePackBuilder.TryGetStrategy(path, out var strategy) &&
        ReplacementFactory.BlankReplacement(strategy, BlankStyle.Template) is { } template
            ? template
            : throw new ArgumentException($"no blanking template for {path}", nameof(path));

    /// <summary>The file now contains exactly the template for its own format.</summary>
    public static bool IsTemplated(string path, byte[] live) => live.AsSpan().SequenceEqual(TemplateFor(path));

    public static bool IsEmptyBytes(byte[] live) => live.Length == 0;

    /// <summary>
    /// Whether a state entry's recorded fingerprint is exactly this path's template — i.e. apply
    /// wrote a valid empty resource and still knows where the original bytes live. Safe to call on
    /// every entry: a non-blanking path (the `.ao` scripts) simply answers false.
    /// </summary>
    public static bool MatchesAppliedTemplate(PatchedFileEntry entry)
    {
        if (entry.OriginalSize <= entry.AppliedSize)
            return false;

        if (!RulePackBuilder.TryGetStrategy(entry.Path, out var strategy))
            return false;

        var template = ReplacementFactory.BlankReplacement(strategy, BlankStyle.Template);
        return template != null &&
               entry.AppliedSize == template.Length &&
               entry.AppliedSha256.Equals(HashUtil.Sha256Bytes(template), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fake original content: clearly bigger than any template, and different per path so a file
    /// cannot be mistaken for its own template or for another file's blanked state.
    /// </summary>
    public static byte[] Fixture(string path)
    {
        var marker = $"fake-asset {path} ";
        var bytes = new byte[Math.Max(FixtureSizeFloor, marker.Length + 64)];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(marker[i % marker.Length] + i % 17);
        return bytes;
    }

    /// <summary>A plausible "the game rewrote this file" body: also outside every template length.</summary>
    public static byte[] Updated(int seed)
    {
        var bytes = new byte[96 + seed];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(0xA0 + (i * 7 + seed) % 53);
        return bytes;
    }
}
