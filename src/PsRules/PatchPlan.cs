using System.Text.Json.Serialization;

namespace PsRules;

public sealed record PlannedChange(
    string Path,
    StrategyKind Strategy,
    long Size,
    string CategoryId,
    RiskTier Tier,
    bool Experimental,
    // Compression transforms are computed once at plan time (they may need cross-file data,
    // e.g. a LOD sibling) and applied verbatim. Never serialized: plan JSON stays byte-stable.
    [property: JsonIgnore] byte[]? Replacement = null);

public sealed class PatchPlan
{
    public string Preset { get; init; } = "safe";
    public string RulePackName { get; init; } = "";
    public List<PlannedChange> Changes { get; } = [];
    public List<string> Missing { get; } = [];
    public List<string> Ignored { get; } = [];
    public List<string> Unsupported { get; } = [];
    public List<string> Unchanged { get; } = [];
    /// <summary>
    /// Every rule skipped for being above the requested tier. The name is kept because the CLI
    /// and GUI labels (and the shell/PowerShell test gates) are pinned to "experimental".
    /// </summary>
    public List<string> SkippedExperimental { get; } = [];

    /// <summary>Split of <see cref="SkippedExperimental"/> by tier, for wording and confirmation dialogs.</summary>
    public int SkippedFxCount { get; set; }

    public int SkippedMaterialCount { get; set; }

    public int TotalFiles => Changes.Count;

    /// <summary>Original size of everything that would be rewritten — for blanking this is what gets destroyed.</summary>
    public long TotalBytes => Changes.Sum(c => c.Size);

    public int MaterialFiles => Changes.Count(c => c.Tier == RiskTier.Material);

    public int CompressionFiles => Changes.Count(c => CompressionOptions.IsCompressionStrategy(c.Strategy));

    /// <summary>Compress-first candidates that fell back to blanking: CategoryId "compression" but a
    /// blanking strategy. Reported separately so the confirmation dialog can distinguish them.</summary>
    public int CompressionFallbackFiles => Changes.Count(c =>
        c.CategoryId == "compression" && !CompressionOptions.IsCompressionStrategy(c.Strategy));

    public long MaterialBytes => Changes.Where(c => c.Tier == RiskTier.Material).Sum(c => c.Size);
}