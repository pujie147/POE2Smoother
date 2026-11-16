namespace PsRules.Compression;

/// <summary>
/// BC1 (DXT1) block encoder/decoder. Alpha-aware: opaque blocks use the 4-color mode, cutout
/// blocks (alpha only 0/255) use the 3-color mode whose index 3 is transparent black, so a
/// texture's hard silhouette survives. Smooth alpha is NOT representable here — the caller
/// (DdsCodec) refuses smooth-alpha textures for BC1 and routes them to BC3 instead.
/// Endpoint/interp math mirrors the validated compress.py prototype (floor rounding).
/// </summary>
public static class Bc1Encoder
{
    /// <summary>Encode a width*height*4 RGBA buffer into block-linear BC1 (8 bytes/block).</summary>
    public static byte[] EncodeImage(byte[] rgba, int width, int height)
    {
        var bw = (width + 3) / 4;
        var bh = (height + 3) / 4;
        var result = new byte[bw * bh * 8];
        Span<byte> block = stackalloc byte[64];
        Span<byte> encoded = stackalloc byte[8];
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
                EncodeBlock(block, encoded);
                encoded.CopyTo(result.AsSpan(pos, 8));
                pos += 8;
            }
        return result;
    }

    /// <summary>Encode one 4x4 block (64 RGBA bytes) into 8 BC1 bytes, preserving 0/255 alpha.</summary>
    public static void EncodeBlock(ReadOnlySpan<byte> rgba, Span<byte> output)
    {
        var anyTransparent = false;
        for (var i = 0; i < 16; i++)
            if (rgba[i * 4 + 3] == 0) { anyTransparent = true; break; }

        if (!anyTransparent)
            EncodeOpaqueBlock(rgba, output);
        else
            EncodeCutoutBlock(rgba, output);
    }

    /// <summary>
    /// The opaque 4-color color block used by BC1 and by BC3's color half. Alpha is ignored:
    /// BC3 carries alpha in a separate BC4 block, so its color endpoints must stay in 4-color mode.
    /// </summary>
    public static void EncodeOpaqueBlock(ReadOnlySpan<byte> rgba, Span<byte> output)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        for (var i = 0; i < 16; i++)
        {
            var o = i * 4;
            int r = rgba[o], g = rgba[o + 1], b = rgba[o + 2];
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
        }

        var c0 = To565(maxR, maxG, maxB);
        var c1 = To565(minR, minG, minB);
        if (c0 < c1) (c0, c1) = (c1, c0);
        if (c0 == c1)
        {
            if (c0 < 65535) c0++;
            else c1--;
        }

        Span<int> pal = stackalloc int[12]; // 4 colors x RGB
        BuildPalette(c0, c1, fourColor: true, pal);
        WriteIndices(rgba, pal, 4, transparent: false, output, c0, c1);
    }

    private static void EncodeCutoutBlock(ReadOnlySpan<byte> rgba, Span<byte> output)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        var anyOpaque = false;
        for (var i = 0; i < 16; i++)
        {
            var o = i * 4;
            if (rgba[o + 3] == 0) continue;
            anyOpaque = true;
            int r = rgba[o], g = rgba[o + 1], b = rgba[o + 2];
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
        }

        if (!anyOpaque)
        {
            // Fully transparent block: c0 == c1 == 0 (3-color mode), every texel index 3.
            output[0] = 0; output[1] = 0; output[2] = 0; output[3] = 0;
            output[4] = 0xFF; output[5] = 0xFF; output[6] = 0xFF; output[7] = 0xFF;
            return;
        }

        var c0 = To565(maxR, maxG, maxB);
        var c1 = To565(minR, minG, minB);
        if (c0 > c1) (c0, c1) = (c1, c0); // 3-color mode requires c0 <= c1
        if (c0 == c1)
        {
            if (c1 < 65535) c1++;
            else c0--;
        }

        Span<int> pal = stackalloc int[12];
        BuildPalette(c0, c1, fourColor: false, pal);
        WriteIndices(rgba, pal, 3, transparent: true, output, c0, c1);
    }

    private static void WriteIndices(ReadOnlySpan<byte> rgba, ReadOnlySpan<int> pal, int paletteSize,
        bool transparent, Span<byte> output, int c0, int c1)
    {
        uint bits = 0;
        for (var i = 0; i < 16; i++)
        {
            var o = i * 4;
            int idx;
            if (transparent && rgba[o + 3] == 0)
            {
                idx = 3;
            }
            else
            {
                int r = rgba[o], g = rgba[o + 1], b = rgba[o + 2];
                idx = 0;
                var best = int.MaxValue;
                for (var j = 0; j < paletteSize; j++)
                {
                    var dr = r - pal[j * 3];
                    var dg = g - pal[j * 3 + 1];
                    var db = b - pal[j * 3 + 2];
                    var d = dr * dr + dg * dg + db * db;
                    if (d < best) { best = d; idx = j; }
                }
            }
            bits |= (uint)idx << (2 * i);
        }

        output[0] = (byte)(c0 & 255);
        output[1] = (byte)(c0 >> 8);
        output[2] = (byte)(c1 & 255);
        output[3] = (byte)(c1 >> 8);
        output[4] = (byte)(bits & 255);
        output[5] = (byte)((bits >> 8) & 255);
        output[6] = (byte)((bits >> 16) & 255);
        output[7] = (byte)((bits >> 24) & 255);
    }

    private static int To565(int r, int g, int b) => ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);

    private static void Expand565(int c, Span<int> rgb)
    {
        var r = (c >> 11) & 31;
        var g = (c >> 5) & 63;
        var b = c & 31;
        rgb[0] = (r << 3) | (r >> 2);
        rgb[1] = (g << 2) | (g >> 4);
        rgb[2] = (b << 3) | (b >> 2);
    }

    private static void BuildPalette(int c0, int c1, bool fourColor, Span<int> pal)
    {
        Span<int> a = stackalloc int[3];
        Span<int> b = stackalloc int[3];
        Expand565(c0, a);
        Expand565(c1, b);
        pal[0] = a[0]; pal[1] = a[1]; pal[2] = a[2];
        pal[3] = b[0]; pal[4] = b[1]; pal[5] = b[2];
        if (fourColor)
        {
            for (var k = 0; k < 3; k++)
            {
                pal[6 + k] = (2 * a[k] + b[k]) / 3;
                pal[9 + k] = (a[k] + 2 * b[k]) / 3;
            }
        }
        else
        {
            for (var k = 0; k < 3; k++)
                pal[6 + k] = (a[k] + b[k]) / 2;
            pal[9] = 0; pal[10] = 0; pal[11] = 0; // index 3 = transparent black
        }
    }

    /// <summary>Decode block-linear BC1 into a width*height*4 RGBA buffer (round-trip verification).</summary>
    public static byte[] DecodeImage(byte[] data, int width, int height)
    {
        var result = new byte[width * height * 4];
        var bw = (width + 3) / 4;
        var bh = (height + 3) / 4;
        var pos = 0;
        Span<int> pal = stackalloc int[16]; // 4 colors x RGBA
        for (var by = 0; by < bh; by++)
            for (var bx = 0; bx < bw; bx++)
            {
                var c0 = data[pos] | (data[pos + 1] << 8);
                var c1 = data[pos + 2] | (data[pos + 3] << 8);
                var bits = (uint)(data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24));
                pos += 8;
                DecodePalette(c0, c1, pal);
                for (var ty = 0; ty < 4; ty++)
                {
                    var y = by * 4 + ty;
                    if (y >= height) { bits >>= 8; continue; }
                    for (var tx = 0; tx < 4; tx++)
                    {
                        var x = bx * 4 + tx;
                        var idx = (int)(bits & 3);
                        bits >>= 2;
                        if (x >= width) continue;
                        var dst = (y * width + x) * 4;
                        var p = idx * 4;
                        result[dst] = (byte)pal[p];
                        result[dst + 1] = (byte)pal[p + 1];
                        result[dst + 2] = (byte)pal[p + 2];
                        result[dst + 3] = (byte)pal[p + 3];
                    }
                }
            }
        return result;
    }

    private static void DecodePalette(int c0, int c1, Span<int> pal)
    {
        Span<int> a = stackalloc int[3];
        Span<int> b = stackalloc int[3];
        Expand565(c0, a);
        Expand565(c1, b);
        pal[0] = a[0]; pal[1] = a[1]; pal[2] = a[2]; pal[3] = 255;
        pal[4] = b[0]; pal[5] = b[1]; pal[6] = b[2]; pal[7] = 255;
        if (c0 > c1)
        {
            for (var k = 0; k < 3; k++)
            {
                pal[8 + k] = (2 * a[k] + b[k]) / 3;
                pal[12 + k] = (a[k] + 2 * b[k]) / 3;
            }
            pal[11] = 255; pal[15] = 255;
        }
        else
        {
            for (var k = 0; k < 3; k++)
                pal[8 + k] = (a[k] + b[k]) / 2;
            pal[11] = 255;
            pal[12] = 0; pal[13] = 0; pal[14] = 0; pal[15] = 0;
        }
    }
}
