using System.Text;

namespace PsRules;

/// <summary>
/// What a blanking strategy writes. <see cref="Template"/> is the default: a minimal document that
/// the format's own loader accepts. <see cref="EmptyBytes"/> is the historical behaviour (a
/// zero-length file), kept so both can be compared on a live client without a rebuild.
/// </summary>
public enum BlankStyle
{
    Template,
    EmptyBytes
}

/// <summary>
/// The settings/CLI/profile spelling of <see cref="BlankStyle"/>. One mapping, because the same
/// string is written by the GUI, read back after a restart, saved inside a profile and typed on the
/// command line — three places inventing their own vocabulary is how they drift.
/// </summary>
public static class BlankStyles
{
    public static string ToKey(BlankStyle style) => style == BlankStyle.EmptyBytes ? "empty" : "template";

    public static bool TryParse(string? value, out BlankStyle style)
    {
        style = value?.Trim().ToLowerInvariant() switch
        {
            "empty" or "emptybytes" or "zero" => BlankStyle.EmptyBytes,
            "template" or "valid" => BlankStyle.Template,
            _ => (BlankStyle)(-1)
        };
        return style is BlankStyle.Template or BlankStyle.EmptyBytes;
    }

    /// <summary>Unknown or absent means template: falling back to zero-length bytes for a settings
    /// file written before this option existed would silently downgrade an existing install.</summary>
    public static BlankStyle FromKey(string? value) =>
        TryParse(value, out var style) ? style : BlankStyle.Template;
}

/// <summary>
/// Minimal valid "this resource is deliberately empty" documents, one per blankable format.
///
/// The point is to stop depending on information we cannot get. A zero-length file is only safe if
/// nothing loads it — and the client's index stores no dependency table at all, while
/// <c>.ot</c>/mesh referrers are formats this project cannot parse. A valid empty document is safe
/// for *any* referrer: whatever loads it gets a resource that renders nothing, not a parse failure.
///
/// The shape is not invented: the client ships its own null assets in exactly this form, and the
/// version numbers are the current schema version of each format
/// (<c>…/mat/empty.mat</c> = 42 B material, <c>…/graphs/general/renderorder_02.fxgraph</c> = 42 B
/// graph, <c>…/epks/empty.epk</c> = 2 B, <c>…/fx/00_null.pet</c> = 4 B; two copies of the last
/// graph in different directories share one hash, so this is an idiom rather than an accident).
/// They are re-authored here from that observation rather than copied out of the archive — the repo
/// deliberately ships no game data (see README "本项目不复用 … 授权规则数据").
/// </summary>
public static class BlankTemplates
{
    private static readonly byte[] EffectPackBytes = U16Le("");
    private static readonly byte[] PetBytes = U16Le("0");
    private static readonly byte[] MaterialBytes = U16Le("{\n    \"version\": 4\n}");
    private static readonly byte[] GraphBytes = U16Le("{\n    \"version\": 3\n}");

    /// <summary>Effect pack: header only. GGG's own <c>empty.epk</c> is these same 2 bytes.</summary>
    public static byte[] EffectPack => (byte[])EffectPackBytes.Clone();

    /// <summary>Particle system: the format's version line carries a bare <c>0</c>.</summary>
    public static byte[] Pet => (byte[])PetBytes.Clone();

    /// <summary>Material: valid JSON declaring the current schema and no graphs.</summary>
    public static byte[] Material => (byte[])MaterialBytes.Clone();

    /// <summary>Effect graph: valid JSON declaring the current schema and no nodes.</summary>
    public static byte[] Graph => (byte[])GraphBytes.Clone();

    public static bool IsBlanking(StrategyKind strategy) =>
        strategy is StrategyKind.Empty or StrategyKind.NullPet or StrategyKind.NullMaterial
            or StrategyKind.NullGraph or StrategyKind.NullEffectPack;

    /// <summary>Every one of these assets is UTF-16LE with a BOM; LF, four-space indent, no trailing newline.</summary>
    internal static byte[] U16Le(string body)
    {
        var bytes = new byte[2 + Encoding.Unicode.GetByteCount(body)];
        bytes[0] = 0xFF;
        bytes[1] = 0xFE;
        Encoding.Unicode.GetBytes(body, 0, body.Length, bytes, 2);
        return bytes;
    }
}
