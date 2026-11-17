using System.Text.Json;

namespace PsRules;

/// <summary>
/// Shipped Chinese display names for effect categories, keyed by the category id that
/// <see cref="RulePackBuilder.Classify"/> produces.
///
/// Resolved at *display* time and deliberately not written into the pack JSON: rebuilding a pack
/// means re-scanning the whole GGPK, so baking labels in would make every wording fix cost the
/// player a rebuild. The trade-off is that the pack on disk and the screen can disagree about zh —
/// the screen is what the user reads, and <c>en</c> stays authoritative for old packs.
///
/// Two tables, because provenance matters: <see cref="OfficialAll"/> is copied verbatim out of the
/// client's own name tables (currently 繁體), <see cref="NamedAll"/> is our translation of folders
/// those tables have no row for. Merging them into one file with a "source" field would let the two
/// drift apart silently, and an invented label presented as official is the mistake this splits to
/// prevent (see docs/zh-labels.md).
/// </summary>
public static class EffectLabels
{
    /// <summary>Manifest resource names, in precedence order: official first, ours second.</summary>
    private const string OfficialResource = "PsRules.Labels.labels-zh.json";
    private const string NamedResource = "PsRules.Labels.labels-zh-named.json";

    /// <summary>
    /// Third table, different key space: <c>effect folder path → official name</c>, for the ground
    /// effects the Ignore tab lists per folder. Category ids cannot carry those rows (one category
    /// holds a dozen folders), and it holds only verbatim client strings — the same no-invention rule
    /// as <see cref="OfficialResource"/>, which is why it is a separate file and not more rows in
    /// <see cref="NamedResource"/>.
    /// </summary>
    private const string EffectDirResource = "PsRules.Labels.ground-effects-zh.json";

    private static readonly Lazy<Tables> Loaded = new(Build);

    private sealed record Tables(
        Dictionary<string, string> Official,
        Dictionary<string, string> Named,
        Dictionary<string, string> Merged,
        Dictionary<string, string> EffectDirs);

    /// <summary>Names copied verbatim from the client's tables.</summary>
    public static IReadOnlyDictionary<string, string> OfficialAll => Loaded.Value.Official;

    /// <summary>Names we translated because the client has no row for that folder.</summary>
    public static IReadOnlyDictionary<string, string> NamedAll => Loaded.Value.Named;

    /// <summary>Number of ids with a shipped Chinese name. Used for the coverage line in <c>pscli list-rules</c>.</summary>
    public static int Count => Loaded.Value.Merged.Count;

    /// <summary>Every shipped id → name, official winning. Used by diagnostics and the table's tests.</summary>
    public static IReadOnlyDictionary<string, string> All => Loaded.Value.Merged;

    public static bool TryGet(string id, out string zh)
    {
        if (!string.IsNullOrWhiteSpace(id) && Loaded.Value.Merged.TryGetValue(id, out var value))
        {
            zh = value;
            return true;
        }

        zh = "";
        return false;
    }

    public static bool Has(string id) => TryGet(id, out _);

    /// <summary>True when the shipped name for this id is a verbatim copy from the client.</summary>
    public static bool IsOfficial(string id) =>
        !string.IsNullOrWhiteSpace(id) && Loaded.Value.Official.ContainsKey(id);

    /// <summary>Official names for individual effect folders, keyed by path prefix.</summary>
    public static IReadOnlyDictionary<string, string> EffectDirAll => Loaded.Value.EffectDirs;

    /// <summary>Entries in the effect-folder table; the coverage line in <c>pscli list-rules</c>.</summary>
    public static int EffectDirCount => Loaded.Value.EffectDirs.Count;

    /// <summary>
    /// "<c>metadata/effects/spells/ground_effects/holy</c>" → 「奉獻地面」. No frame is added on a miss:
    /// the caller shows the folder name itself, which is what the ignore line will say anyway.
    /// </summary>
    public static bool TryGetEffectDir(string prefix, out string zh)
    {
        if (!string.IsNullOrWhiteSpace(prefix) && Loaded.Value.EffectDirs.TryGetValue(prefix.Trim(), out var value))
        {
            zh = value;
            return true;
        }

        zh = "";
        return false;
    }

    /// <summary>
    /// Chinese frame per id namespace, worded exactly like <see cref="RulePackBuilder.Classify"/> so a
    /// rebuilt pack and an old one on disk read the same. Only namespaces the builder emits are listed:
    /// <c>monsters</c>/<c>misc</c> carry no dot and already have real Chinese names.
    /// </summary>
    /// <remarks>繁體, like both tables — a 简体 frame next to a 繁體 name would look like two bugs.</remarks>
    private static readonly Dictionary<string, string> Frames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skill"] = "技能",
        ["env"] = "環境",
        ["terrain"] = "地形",
        ["mtx"] = "外觀",
        ["league"] = "聯賽"
    };

    /// <summary>Frame words, exposed so the parity test can compare them against <c>Classify</c>.</summary>
    public static IReadOnlyDictionary<string, string> FrameWords => Frames;

    /// <summary>
    /// Frames the builder wrote before this round, when the Chinese column was still 简体. An old
    /// pack stores them in its own JSON, so a category we have no shipped name for would otherwise
    /// render "环境: Aow" — Simplified frame next to a column of Traditional names.
    /// </summary>
    private static readonly (string Old, string New)[] LegacyFrames =
    [
        ("环境材质: ", "環境材質: "),
        ("环境: ", "環境: "),
        ("外观特效: ", "外觀特效: "),
        ("地形: ", "地形: "),
        ("技能: ", "技能: "),
        ("联赛: ", "聯賽: ")
    ];

    /// <summary>Rewrite a stale 简体 frame to the 繁體 one the rest of the column uses.</summary>
    public static string Reframe(string text)
    {
        foreach (var (old, replacement) in LegacyFrames)
        {
            if (text.StartsWith(old, StringComparison.Ordinal))
                return replacement + text[old.Length..];
        }

        return text;
    }

    /// <summary>
    /// English frames <see cref="RulePackBuilder"/> puts in the <c>en</c> label, and their Chinese
    /// equivalents. Stripped before a frame is added, otherwise an old pack would read
    /// "环境: Environment material: Desert".
    /// </summary>
    private static readonly string[] EnglishFrames =
    [
        "Environment material: ", "Environment: ", "Terrain: ", "MTX: ", "Monster skills", "Misc effects"
    ];

    /// <summary>
    /// Han ideographs only — CJK punctuation, 「·」 and full-width brackets do not count, otherwise a
    /// label like "多重 UV 素材" would look Chinese while still being untranslatable to a player.
    /// </summary>
    public static bool ContainsHan(string? text) =>
        text != null && text.Any(c => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '豈' and <= '﫿');

    /// <summary>
    /// "<c>技能: Arc</c>" for a name that arrived in Latin letters from an old pack, or the text
    /// unchanged when the namespace is unknown — inventing a frame for an id the builder never
    /// produces would label foreign packs with categories they do not have.
    /// </summary>
    public static string WithFrame(string id, string name)
    {
        var dot = id.IndexOf('.');
        if (dot <= 0 || !Frames.TryGetValue(id[..dot], out var frame))
            return name;

        var bare = name;
        foreach (var prefix in EnglishFrames)
        {
            if (bare.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                bare = bare[prefix.Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(bare))
            return name;

        return $"{frame}: {bare.Trim()}";
    }

    /// <summary>
    /// A missing or malformed table must degrade to "no Chinese names", never to a tool that cannot
    /// list categories — the label table is polish, category access is the product. The two files are
    /// independent: if one fails to parse, the other still contributes.
    /// </summary>
    private static Tables Build()
    {
        var official = LoadFile(OfficialResource);
        var named = LoadFile(NamedResource);
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, zh) in official)
            merged[id] = zh;
        foreach (var (id, zh) in named)
            merged.TryAdd(id, zh);

        return new Tables(official, named, merged, LoadFile(EffectDirResource));
    }

    private static Dictionary<string, string> LoadFile(string resourceName)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = typeof(EffectLabels).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return labels;

            var document = JsonSerializer.Deserialize<LabelFile>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (document?.Labels == null)
                return labels;

            foreach (var (id, zh) in document.Labels)
            {
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(zh))
                    labels[id.Trim()] = zh.Trim();
            }
        }
        catch (Exception)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return labels;
    }

    private sealed class LabelFile
    {
        public int Version { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
    }
}
