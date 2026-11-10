namespace PsRules;

public sealed record PlannedChange(
    string Path,
    StrategyKind Strategy,
    long Size,
    string CategoryId,
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
    public List<string> SkippedExperimental { get; } = [];

    public int TotalFiles => Changes.Count;

    public long TotalBytes => Changes.Sum(c => c.Size);
}