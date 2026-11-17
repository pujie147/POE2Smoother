namespace PsRules;

/// <summary>
/// The ground-effect prefixes a player can protect, and the *text-level* edit the ignore-list box
/// needs for the tree on the Ignore tab.
///
/// Why text-level instead of <c>IgnoreList.FromLines(box.Text)</c> → edit → join back:
/// <see cref="IgnoreList.Normalize"/> returns "" for every <c>#</c> line (IgnoreList.cs:93) and
/// <c>Add</c> dedupes (58), so one round-trip through the object silently deletes the comments and
/// blank lines the player wrote. The box *is* the storage the tree derives from, so it is edited
/// line by line and every foreign line stays verbatim, in place.
///
/// Coverage is <see cref="IgnoreList.IsIgnored"/>'s own rule: a line matches a path only when the
/// path equals it or continues with <c>/</c> (IgnoreList.cs:82-84). The UI depends on both halves:
/// ticking a branch writes ONE line and every leaf under it then reads as ticked (inherited, so a
/// leaf cannot be un-ticked on its own), and <c>…/ground_effects</c> does NOT cover
/// <c>…/ground_effects_v2</c> — which is why the top node writes the three category lines instead
/// of one root line.
/// </summary>
public static class GroundEffectProtection
{
    public const string GroundEffectsV1 = "metadata/effects/spells/ground_effects";
    public const string GroundEffectsV2 = "metadata/effects/spells/ground_effects_v2";
    public const string GroundEffectsV3 = "metadata/effects/spells/ground_effects_v3";

    private static readonly string[] Categories = [GroundEffectsV1, GroundEffectsV2, GroundEffectsV3];

    /// <summary>The three category roots; the top tree node is exactly these three lines.</summary>
    public static IReadOnlyList<string> CategoryPrefixes => Categories;

    /// <summary>True when the text carries <paramref name="prefix"/> as a line of its own (not merely
    /// covered by something above it). That difference is what decides whether a tree row can be
    /// un-ticked: a leaf protected by its branch's line has no line of its own to remove.</summary>
    public static bool Lists(string? boxText, string prefix)
    {
        var target = IgnoreList.Normalize(prefix ?? "");
        if (target.Length == 0)
            return false;

        return (boxText ?? "").Split('\n')
            .Any(line => IgnoreList.Normalize(line).Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The line that already covers <paramref name="prefix"/> — itself, or the shortest
    /// ancestor line above it. Null when nothing protects it.</summary>
    public static string? CoveringLine(string? boxText, string prefix)
    {
        var target = IgnoreList.Normalize(prefix ?? "");
        if (target.Length == 0)
            return null;

        string? ancestor = null;
        foreach (var raw in (boxText ?? "").Split('\n'))
        {
            // Normalize drops comments and blank lines, so a commented-out prefix never counts.
            var line = IgnoreList.Normalize(raw);
            if (line.Length == 0)
                continue;

            if (line.Equals(target, StringComparison.OrdinalIgnoreCase))
                return line;
            if (target.StartsWith(line + "/", StringComparison.OrdinalIgnoreCase) &&
                (ancestor == null || line.Length < ancestor.Length))
                ancestor = line;
        }

        return ancestor;
    }

    public static bool Covers(string? boxText, string prefix) => CoveringLine(boxText, prefix) != null;

    /// <summary>Same question asked of a live list rather than the box text.</summary>
    public static bool Covers(IgnoreList list, string prefix) => list.IsIgnored(prefix);

    public static bool CoversAll(string? boxText, IEnumerable<string> prefixes) =>
        prefixes.All(prefix => Covers(boxText, prefix));

    /// <summary>
    /// Tick or un-tick <paramref name="prefix"/> in the box text, preserving every other line
    /// verbatim and in order. Both directions collapse the subtree to one line: the descendants go
    /// whether we are adding the line (it would be redundant twice over) or removing it (a branch
    /// can read as ticked *because* all its leaves are ticked, and leaving those lines behind would
    /// make the click appear to do nothing — the derivation would just tick it straight back).
    /// A leaf under a ticked branch cannot be un-ticked at all: it is rendered disabled and its
    /// tooltip names the line that covers it, because "remove the ancestor" is not a thing a click
    /// on the child can honestly mean.
    /// Returns the input unchanged when nothing would move — that keeps the text → checkbox → text
    /// loop from writing the box back on every keystroke.
    /// </summary>
    public static string Apply(string? boxText, string prefix, bool on)
        => ApplyMany(boxText, [prefix], on);

    public static string ApplyMany(string? boxText, IEnumerable<string> prefixes, bool on)
    {
        var targets = prefixes
            .Select(p => IgnoreList.Normalize(p))
            .Where(p => p.Length > 0)
            .ToList();
        if (targets.Count == 0)
            return boxText ?? "";

        var text = boxText ?? "";
        // The box is joined with Environment.NewLine but parsed with Split('\n') everywhere
        // (MainWindow.axaml.cs:569, 860, 895); keep whichever ending this text already uses.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        var kept = new List<string>(lines.Count);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var normalized = IgnoreList.Normalize(line);
            if (normalized.Length == 0)
            {
                kept.Add(line);                     // blank lines and # comments stay exactly where they are
                continue;
            }

            if (targets.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                if (!on)
                    continue;                       // un-ticking: this line goes
                written.Add(normalized);            // already there — keep its position
                kept.Add(line);
                continue;
            }

            if (IsUnderAny(normalized, targets))
                continue;                           // collapse: the subtree is now one line, either way

            kept.Add(line);
        }

        if (on)
        {
            var insert = kept.Count;
            foreach (var target in targets)
            {
                if (written.Contains(target))
                    continue;

                // Behind the last line that carries content: a player's trailing blank line stays
                // trailing, and their comment block stays attached to what it documents.
                while (insert > 0 && kept[insert - 1].Trim().Length == 0)
                    insert--;
                kept.Insert(insert++, target);
            }
        }

        return SameLines(kept, lines) ? text : string.Join(newline, kept);
    }

    /// <summary>
    /// Add the lines the text does not carry yet, touching nothing else — no collapse, no reorder.
    /// This is what a rule-pack rebuild needs: the pack's <c>Ignore</c> array is the built-in default
    /// list (<c>AppService.BuildRules</c> passes no <see cref="IgnoreList"/>), and merging it is the
    /// difference between "the defaults are back" and "your ground-effect ticks are gone".
    /// </summary>
    public static string AppendMissing(string? boxText, IEnumerable<string> prefixes)
    {
        var text = boxText ?? "";
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var known = new HashSet<string>(
            lines.Select(IgnoreList.Normalize).Where(l => l.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var raw in prefixes)
        {
            var prefix = IgnoreList.Normalize(raw);
            if (prefix.Length == 0 || !known.Add(prefix))
                continue;

            lines.Add(prefix);
            added = true;
        }

        return added ? string.Join(text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", lines) : text;
    }

    private static bool IsUnderAny(string normalized, IReadOnlyList<string> targets) =>
        targets.Any(t => normalized.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase));

    private static bool SameLines(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
