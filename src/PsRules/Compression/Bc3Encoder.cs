namespace PsRules.Compression;

/// <summary>
/// BC3 (DXT5) encoder/decoder: a BC4 alpha block (8 bytes, 8-bit endpoints + 3-bit indices)
/// followed by a BC1 4-color color block (8 bytes). Used by the aggressive downscale tier for
/// smooth-alpha textures so soft particle edges keep their gradient instead of being hardened
/// to BC1 cutout. Same byte size as BC7, so it is only a win when combined with downscaling.
/// </summary>
public static class Bc3Encoder
{
    public static byte[] EncodeImage(byte[] rgba, int width, int height)
    {
        var bw = (width + 3) / 4;
        var bh = (height + 3) / 4;
        var result = new byte[bw * bh * 16];
        Span<byte> block = stackalloc byte[64];
        Span<byte> color = stackalloc byte[8];
        var pos = 0;
        for (var by = 0; by < bh; by++)
            for (var bx = 0; bx < bw; bx++)
            {
                for (var ty = 0; ty < 4; ty++)
                {
                    var y = Math.Min(by * 4 + ty, height - 1);
                    for (var tx = 0; tx < 4; tx++)
                    {
                        var x = Math.Min(bx * 4 + tx, width - 1);
                        var src = (y * width + x) * 4;
                        var dst = (ty * 4 + tx) * 4;
                        block[dst] = rgba[src];
                        block[dst + 1] = rgba[src + 1];
                        block[dst + 2] = rgba[src + 2];
                        block[dst + 3] = rgba[src + 3];
                    }
                }
                EncodeAlphaBlock(block, result.AsSpan(pos, 8));
                Bc1Encoder.EncodeOpaqueBlock(block, color);
                color.CopyTo(result.AsSpan(pos + 8, 8));
                pos += 16;
            }
        return result;
    }

    private static void EncodeAlphaBlock(ReadOnlySpan<byte> rgba, Span<byte> output)
    {
        int a0 = 0, a1 = 255;
        for (var i = 0; i < 16; i++)
        {
            var a = rgba[i * 4 + 3];
            if (a > a0) a0 = a;
            if (a < a1) a1 = a;
        }

        Span<int> pal = stackalloc int[8];
        BuildAlphaPalette(a0, a1, pal);

        ulong bits = 0;
        for (var i = 0; i < 16; i++)
        {
            var a = rgba[i * 4 + 3];
            var best = 0;
            var bestDist = int.MaxValue;
            for (var j = 0; j < 8; j++)
            {
                var d = Math.Abs(a - pal[j]);
                if (d < bestDist) { bestDist = d; best = j; }
            }
            bits |= (ulong)best << (3 * i);
        }

        output[0] = (byte)a0;
        output[1] = (byte)a1;
        for (var k = 0; k < 6; k++)
            output[2 + k] = (byte)((bits >> (8 * k)) & 255);
    }

    private static void BuildAlphaPalette(int a0, int a1, Span<int> pal)
    {
        pal[0] = a0;
        pal[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i <= 6; i++)
                pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        }
        else
        {
            for (var i = 1; i <= 4; i++)
                pal[i + 1] = ((5 - i) * a0 + i * a1) / 5;
            pal[6] = 0;
            pal[7] = 255;
        }
    }

    /// <summary>Decode block-linear BC3 into a width*height*4 RGBA buffer (round-trip verification).</summary>
    public static byte[] DecodeImage(byte[] data, int width, int height)
    {
        var color = Bc1Encoder.DecodeImage(ExtractColorBlocks(data, width, height), width, height);
        var result = color;
        var bw = (width + 3) / 4;
        var bh = (height + 3) / 4;
        var pos = 0;
        Span<int> pal = stackalloc int[8];
        for (var by = 0; by < bh; by++)
            for (var bx = 0; bx < bw; bx++)
            {
                var a0 = data[pos];
                var a1 = data[pos + 1];
                BuildAlphaPalette(a0, a1, pal);
                ulong bits = 0;
                for (var k = 0; k < 6; k++)
                    bits |= (ulong)data[pos + 2 + k] << (8 * k);
                pos += 16;
                for (var ty = 0; ty < 4; ty++)
                {
                    var y = by * 4 + ty;
                    if (y >= height) { bits >>= 12; continue; }
                    for (var tx = 0; tx < 4; tx++)
                    {
                        var x = bx * 4 + tx;
                        var idx = (int)(bits & 7);
                        bits >>= 3;
                        if (x >= width) continue;
                        result[(y * width + x) * 4 + 3] = (byte)pal[idx];
                    }
                }
            }
        return result;
    }

    // BC3 interleaves alpha+color per block; BC1 decode wants the color blocks packed together.
    private static byte[] ExtractColorBlocks(byte[] data, int width, int height)
    {
        var blocks = ((width + 3) / 4) * ((height + 3) / 4);
        var color = new byte[blocks * 8];
        for (var i = 0; i < blocks; i++)
            Array.Copy(data, i * 16 + 8, color, i * 8, 8);
        return color;
    }
}
