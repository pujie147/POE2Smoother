using System.Text;
using System.Text.RegularExpressions;

namespace PsRules;

public static class ReplacementFactory
{
    // Real client scripts use all of these shapes (verified against production .ao bodies):
    //   this.attached = parent_ao.AddAttached( '<path>', <lock>);
    //   parent_ao.RemoveEffectPack( "<path>" );
    //   IfState( body, 1, { parent_ao.AddEffectPack( "<path>" ); } );
    // Both quote styles occur, and the second argument is a placeholder whose name varies
    // (<root>, <lock>), so it is matched as <...> rather than hardcoded to one spelling.
    private const string CallShape =
        @"(?:AddAttached|AddEffectPack|RemoveEffectPack)\s*\(\s*(?:'[^']*'|""[^""]*"")\s*(?:,\s*<[^>]*>\s*)?\)";

    // Optional receiver (`parent_ao.`, `enemy.`) glued to the call name. It cannot cross
    // whitespace, `(`, `,`, `;`, `{`, `}` or `=`, so it never swallows a surrounding block.
    private const string Receiver = @"(?:[^\s(,;{}=]+[.:]\s*)?";

    // An assignment whose entire right-hand side is one of these calls, ending the statement.
    // Deleting only the call here would leave `this.attached = parent_ao.` — a syntax error —
    // so the whole right-hand side collapses to `nil` and the variable stays defined for the
    // paired `RemoveAttached( this.attached );` that the same script usually calls later.
    private static readonly Regex AttachedAssignment = new(
        @"(?<lhs>[^\s=][^=\n]*?=\s*)" + Receiver + CallShape + @"\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // A call used as a standalone statement expression, or as the only content of a block:
    // receiver, call and trailing semicolon all go away together, leaving valid syntax.
    private static readonly Regex EffectCall = new(
        Receiver + CallShape + @"\s*;?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Lazy<byte[]> TransparentDdsLazy = new(CreateTransparentDds);

    public static byte[] TransparentDds => TransparentDdsLazy.Value;

    public static byte[] Create(ReadOnlySpan<byte> original, string path, StrategyKind strategy)
    {
        if (strategy == StrategyKind.Auto)
        {
            if (!RulePackBuilder.TryGetStrategy(path, out strategy))
                strategy = StrategyKind.Keep;
        }

        return strategy switch
        {
            StrategyKind.Keep => original.ToArray(),
            StrategyKind.Empty => [],
            StrategyKind.NullPet => [],
            StrategyKind.NullMaterial => [],
            StrategyKind.NullGraph => [],
            StrategyKind.NullEffectPack => [],
            StrategyKind.TransparentDds => TransparentDds,
            StrategyKind.StripAttachments => StripAttachments(original),
            _ => original.ToArray()
        };
    }

    public static byte[] StripAttachments(ReadOnlySpan<byte> original)
    {
        if (original.IsEmpty)
            return [];

        // PoE2 .ao scripts are UTF-16LE with a BOM; strip them in their own encoding.
        // Do not guess on other binary assets; leave them untouched rather than corrupting them.
        Encoding encoding;
        if (original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
        }
        else
        {
            if (original.Contains((byte)0))
                return original.ToArray();
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        string text;
        try
        {
            text = encoding.GetString(original);
        }
        catch (DecoderFallbackException)
        {
            return original.ToArray();
        }

        // Assignments first: they rewrite the whole right-hand side. The statement-expression
        // pass then only sees calls that are genuinely standalone.
        var replaced = EffectCall.Replace(
            AttachedAssignment.Replace(text, "${lhs}nil;"), "");
        if (ReferenceEquals(replaced, text) || replaced == text)
            return original.ToArray();

        // GetString keeps the U+FEFF character of a BOM'd input, and GetBytes never adds
        // a preamble, so re-encoding with the same Encoding preserves the original layout.
        return encoding.GetBytes(replaced);
    }

    private static byte[] CreateTransparentDds()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0x20534444u); // "DDS "
        writer.Write(124u);        // header size
        writer.Write(0x00081007u); // CAPS | HEIGHT | WIDTH | PIXELFORMAT | LINEARSIZE
        writer.Write(1u);          // height
        writer.Write(1u);          // width
        writer.Write(8u);          // pitch / linear size of one 4x4 BC1 block
        writer.Write(0u);          // depth
        writer.Write(1u);          // mip map count
        for (var i = 0; i < 11; i++)
            writer.Write(0u);      // reserved

        writer.Write(32u);         // pixel format size
        writer.Write(0x4u);        // DDPF_FOURCC
        writer.Write(Encoding.ASCII.GetBytes("DXT1"));
        writer.Write(0u);          // rgb bit count
        writer.Write(0u);          // r mask
        writer.Write(0u);          // g mask
        writer.Write(0u);          // b mask
        writer.Write(0u);          // a mask

        writer.Write(0x1000u);     // caps: TEXTURE
        writer.Write(0u);          // caps2
        writer.Write(0u);          // caps3
        writer.Write(0u);          // caps4
        writer.Write(0u);          // reserved2

        // One BC1 block: color0 == color1 -> 3-color + transparent mode, index 3.
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0x00000003u);

        writer.Flush();
        return stream.ToArray();
    }
}