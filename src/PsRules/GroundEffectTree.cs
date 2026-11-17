namespace PsRules;

/// <summary>
/// The three-level shape behind the ground-effect tree on the Ignore tab:
/// 地面特效 → the three <c>skill.ground_effects*</c> categories → one node per effect folder.
///
/// Built from the loaded rule pack rather than from a hard-coded list, because a folder list rots
/// the moment the client adds or renames one: it would silently hide a new ground effect from
/// protection while still offering ones that no longer exist. The pack is what the tool is about to
/// write, so the tree can only ever show files the player can actually protect.
///
/// Only paths and counts live here; naming is the caller's job
/// (<see cref="EffectLabels.TryGetEffectDir"/> for the shipped official names, the folder segment for
/// everything else) so this stays testable without a UI language in play.
/// </summary>
public static class GroundEffectTree
{
    /// <summary>Category ids <see cref="RulePackBuilder.Classify"/> gives the three ground folders.</summary>
    public static bool IsGroundCategory(string? categoryId) =>
        categoryId?.StartsWith("skill.ground_effects", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Path root for a ground category id. <c>Classify</c> slugs the folder name into the id after
    /// <c>skill.</c>, and the ground folders survive that slug unchanged, so the root is spelled back
    /// out of the id. Callers must still verify a rule lies under it — see <see cref="Build"/>.
    /// </summary>
    public static string RootPrefixFor(string categoryId) =>
        "metadata/effects/spells/" + categoryId["skill.".Length..];

    /// <summary>Branches in category order, leaves in path order. Empty when the pack has none.</summary>
    public static IReadOnlyList<GroundEffectBranch> Build(RulePack? pack)
    {
        if (pack == null)
            return [];

        var branches = new List<GroundEffectBranch>();
        foreach (var category in pack.Categories)
        {
            if (!IsGroundCategory(category.Id))
                continue;

            var root = RootPrefixFor(category.Id);
            var leaves = new Dictionary<string, GroundEffectLeaf>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var outside = 0;

            foreach (var rule in category.Rules)
            {
                var relative = RelativeTo(rule.Path, root);
                if (relative == null)
                {
                    // The id says ground_effects but the path is elsewhere: a hand-written pack, or a
                    // folder rename that predates this build. Counted on the branch, not offered as a
                    // node — ticking a prefix that matches nothing would look like protection.
                    outside++;
                    continue;
                }

                // A file loose under the root keeps its own name as the segment: IsIgnored matches an
                // exact line too, so it still gets a real prefix to tick.
                var slash = relative.IndexOf('/');
                var segment = slash < 0 ? relative : relative[..slash];
                var prefix = root + "/" + segment;

                if (!leaves.TryGetValue(prefix, out var leaf))
                {
                    leaf = new GroundEffectLeaf { Segment = segment, Prefix = prefix };
                    leaves.Add(prefix, leaf);
                    order.Add(prefix);
                }

                leaf.Add(rule.Tier);
            }

            branches.Add(new GroundEffectBranch
            {
                CategoryId = category.Id,
                Prefix = root,
                Leaves = order.Select(key => leaves[key]).ToList(),
                UnlistedCount = outside
            });
        }

        return branches;
    }

    /// <summary>The path under <paramref name="root"/>, or null when the rule is not inside it.</summary>
    private static string? RelativeTo(string path, string root)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Length <= root.Length + 1)
            return null;

        return normalized[(root.Length + 1)..];
    }
}

public sealed class GroundEffectBranch
{
    public required string CategoryId { get; init; }

    /// <summary>Ticking the whole category writes this one line.</summary>
    public required string Prefix { get; init; }

    public required IReadOnlyList<GroundEffectLeaf> Leaves { get; init; }

    /// <summary>Rules of this category that lie outside its own path root; see <see cref="GroundEffectTree.Build"/>.</summary>
    public int UnlistedCount { get; init; }

    public int FileCount => Leaves.Sum(l => l.Count) + UnlistedCount;
}

public sealed class GroundEffectLeaf
{
    /// <summary>Folder name (or file name when nothing sits under the root), straight from the client.</summary>
    public required string Segment { get; init; }

    public required string Prefix { get; init; }

    public int Count { get; private set; }
    public int SafeCount { get; private set; }
    public int FxCount { get; private set; }
    public int MaterialCount { get; private set; }

    internal void Add(RiskTier tier)
    {
        Count++;
        switch (tier)
        {
            case RiskTier.Safe:
                SafeCount++;
                break;
            case RiskTier.Fx:
                FxCount++;
                break;
            default:
                MaterialCount++;
                break;
        }
    }
}
