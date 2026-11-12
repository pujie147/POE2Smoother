namespace PsRules;

public sealed record PlannedChange(
    string Path,
    StrategyKind Strategy,
    long Size,
    string CategoryId,
    RiskTier Tier,
    bool Experimental);

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

    public long MaterialBytes => Changes.Where(c => c.Tier == RiskTier.Material).Sum(c => c.Size);
}