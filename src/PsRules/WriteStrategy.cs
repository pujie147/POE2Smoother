namespace PsRules;

/// <summary>
/// How a file class is rewritten: blank it, compress it, or compress-first with a blanking
/// fallback. This is the user-facing choice that replaces the old two-value <see cref="BlankStyle"/>;
/// <see cref="BlankStyle"/> is still derived from it (see <see cref="WriteStrategies.ToBlankStyle"/>)
/// because <see cref="ReplacementFactory"/> only cares what a blanking strategy writes.
/// </summary>
public enum WriteStrategy
{
    /// <summary>Blank to a minimal valid document. No compression.</summary>
    Template,

    /// <summary>Blank to a zero-length file. No compression.</summary>
    Empty,

    /// <summary>Only render-compress candidates; never blank. Uncompressible files stay untouched.</summary>
    CompressionOnly,

    /// <summary>Compress candidates first; blank to a valid document when compression is impossible.</summary>
    CompressThenTemplate,

    /// <summary>Compress candidates first; blank to zero bytes when compression is impossible.</summary>
    CompressThenEmpty
}

/// <summary>
/// The settings/CLI/profile/GUI spelling of <see cref="WriteStrategy"/>. One mapping for the same
/// reason as <see cref="BlankStyles"/>: four places inventing their own vocabulary is how they drift.
/// </summary>
public static class WriteStrategies
{
    public static string ToKey(WriteStrategy strategy) => strategy switch
    {
        WriteStrategy.Empty => "empty",
        WriteStrategy.CompressionOnly => "compress-only",
        WriteStrategy.CompressThenTemplate => "compress-then-template",
        WriteStrategy.CompressThenEmpty => "compress-then-empty",
        _ => "template"
    };

    public static bool TryParse(string? value, out WriteStrategy strategy)
    {
        strategy = value?.Trim().ToLowerInvariant() switch
        {
            "empty" or "emptybytes" or "zero" => WriteStrategy.Empty,
            "template" or "valid" => WriteStrategy.Template,
            "compress-only" or "compression-only" => WriteStrategy.CompressionOnly,
            "compress-then-template" => WriteStrategy.CompressThenTemplate,
            "compress-then-empty" => WriteStrategy.CompressThenEmpty,
            _ => (WriteStrategy)(-1)
        };
        return strategy is WriteStrategy.Template or WriteStrategy.Empty or WriteStrategy.CompressionOnly
            or WriteStrategy.CompressThenTemplate or WriteStrategy.CompressThenEmpty;
    }

    /// <summary>Unknown or absent means template, matching <see cref="BlankStyles.FromKey"/> so a
    /// settings file written before this option existed is never silently downgraded.</summary>
    public static WriteStrategy FromKey(string? value) =>
        TryParse(value, out var strategy) ? strategy : WriteStrategy.Template;

    /// <summary>What a blanking strategy writes under this strategy. <see cref="WriteStrategy.CompressionOnly"/>
    /// never blanks, so its derived style is irrelevant; template is the safe default.</summary>
    public static BlankStyle ToBlankStyle(WriteStrategy strategy) =>
        strategy is WriteStrategy.Empty or WriteStrategy.CompressThenEmpty
            ? BlankStyle.EmptyBytes
            : BlankStyle.Template;

    /// <summary>Whether the render-compression pass runs at all under this strategy.</summary>
    public static bool IsCompressionContaining(WriteStrategy strategy) =>
        strategy is WriteStrategy.CompressionOnly or WriteStrategy.CompressThenTemplate
            or WriteStrategy.CompressThenEmpty;

    /// <summary>Whether an uncompressible candidate falls back to blanking (vs. staying untouched).</summary>
    public static bool FallsBackToBlank(WriteStrategy strategy) =>
        strategy is WriteStrategy.CompressThenTemplate or WriteStrategy.CompressThenEmpty;
}