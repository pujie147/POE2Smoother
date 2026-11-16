using System.Reflection;
using System.Text.Json;

namespace PsRules;

/// <summary>
/// Shipped 简体中文 display names for effect categories, keyed by the category id that
/// <see cref="RulePackBuilder.Classify"/> produces.
///
/// Resolved at *display* time and deliberately not written into the pack JSON: rebuilding a pack
/// means re-scanning the whole GGPK, so baking labels in would make every wording fix cost the
/// player a rebuild. The trade-off is that the pack on disk and the screen can disagree about zh —
/// the screen is what the user reads, and <c>en</c> stays authoritative for old packs.
/// </summary>
public static class EffectLabels
{
    /// <summary>Manifest resource name; matches <c>Labels/labels-zh.json</c> plus the default root namespace.</summary>
    private const string ResourceName = "PsRules.Labels.labels-zh.json";

    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    /// <summary>Number of ids with a shipped Chinese name. Used for the coverage line in <c>pscli list-rules</c>.</summary>
    public static int Count => Table.Value.Count;

    public static bool TryGet(string id, out string zh)
    {
        if (!string.IsNullOrWhiteSpace(id) && Table.Value.TryGetValue(id, out var value))
        {
            zh = value;
            return true;
        }

        zh = "";
        return false;
    }

    public static bool Has(string id) => TryGet(id, out _);

    /// <summary>Every shipped id → name. Read-only view, used by diagnostics and the table's own tests.</summary>
    public static IReadOnlyDictionary<string, string> All => Table.Value;

    /// <summary>
    /// Chinese frame per id namespace, worded exactly like <see cref="RulePackBuilder.Classify"/> so a
    /// rebuilt pack and an old one on disk read the same. Only namespaces the builder emits are listed:
    /// <c>monsters</c>/<c>misc</c> carry no dot and already have real Chinese names.
    /// </summary>
    private static readonly Dictionary<string, string> Frames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skill"] = "技能",
        ["env"] = "环境",
        ["terrain"] = "地形",
        ["mtx"] = "外观",
        ["league"] = "联赛"
    };

    /// <summary>
    /// English frames <see cref="RulePackBuilder"/> puts in the <c>en</c> label. Stripped before a
    /// Chinese frame is added, otherwise an old pack would read "环境: Environment material: Desert".
    /// </summary>
    private static readonly string[] EnglishFrames =
    [
        "Environment material: ", "Environment: ", "Terrain: ", "MTX: ", "Monster skills", "Misc effects"
    ];

    public static bool ContainsHan(string? text) =>
        text != null && text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

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
    /// list categories — the label table is polish, category access is the product.
    /// </summary>
    private static Dictionary<string, string> Load()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var assembly = typeof(EffectLabels).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName);
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
