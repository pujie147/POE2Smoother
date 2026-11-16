using System.Text;

namespace PsRules.Compression;

/// <summary>
/// Same-resolution DDS recompression (BC7 → BC1, UV-safe) with an optional aggressive downscale.
/// Smooth-alpha textures are refused for same-resolution BC1: BC1 only carries 1-bit alpha and
/// BC3 is the same size as BC7, so there is no honest win there. The downscale tier keeps soft
/// particles soft by routing smooth alpha to BC3 at half resolution; opaque/cutout go to BC1.
/// Only single-array 2D BC7 (DXGI 98) is handled — anything else is left untouched.
/// </summary>
public static class DdsCodec
{
    private enum AlphaKind { Opaque, Cutout, Smooth }

    public static byte[]? TryCompress(byte[] original, bool downscale)
    {
        if (original.Length < 148)
            return null;
        if (original[0] != 'D' || original[1] != 'D' || original[2] != 'S' || original[3] != ' ')
            return null;
        if (original[84] != 'D' || original[85] != 'X' || original[86] != '1' || original[87] != '0')
            return null;

        var dxgi = BitConverter.ToUInt32(original, 128);
        if (dxgi != 98) // BC7_UNORM only; BC1/BC3 are already as small as they get
            return null;
        var dimension = BitConverter.ToUInt32(original, 132);
        var miscFlag = BitConverter.ToUInt32(original, 136);
        var arraySize = BitConverter.ToUInt32(original, 140);
        if (dimension != 3 || arraySize != 1 || (miscFlag & 0x4) != 0) // 2D, non-array, non-cube
            return null;

        var height = (int)BitConverter.ToUInt32(original, 12);
        var width = (int)BitConverter.ToUInt32(original, 16);
        var mipCount = (int)BitConverter.ToUInt32(original, 28);
        if (mipCount == 0) mipCount = 1;
        if (width < 1 || height < 1)
            return null;

        const int off = 148;
        if (original.Length < off + MipChainBytes(width, height, mipCount, 16))
            return null; // truncated / malformed

        var mip0 = Bc7Decoder.DecodeImage(original, off, width, height);

        return downscale
            ? CompressDownscale(original, mip0, width, height)
            : CompressSameResolution(original, mip0, width, height, mipCount, off);
    }

    private static byte[]? CompressSameResolution(byte[] original, byte[] mip0,
        int width, int height, int mipCount, int off)
    {
        // BC1 cannot carry a smooth alpha gradient; refuse rather than harden soft particles.
        if (ClassifyAlpha(mip0) == AlphaKind.Smooth)
            return null;

        var levels = new List<byte[]>(mipCount);
        var cw = width;
        var ch = height;
        var pos = off;
        for (var m = 0; m < mipCount; m++)
        {
            var rgba = m == 0 ? mip0 : Bc7Decoder.DecodeImage(original, pos, cw, ch);
            levels.Add(Bc1Encoder.EncodeImage(rgba, cw, ch));
            pos += BlockBytes(cw, ch, 16);
            cw = Math.Max(1, cw / 2);
            ch = Math.Max(1, ch / 2);
        }

        var result = BuildDx10Dds(levels, width, height, dxgiFormat: 71);
        return result.Length < original.Length ? result : null;
    }

    private static byte[]? CompressDownscale(byte[] original, byte[] mip0, int width, int height)
    {
        if (width < 2 || height < 2)
            return null;

        var (scaled, nw, nh) = Downscale(mip0, width, height);
        // Classify AFTER downscaling: box-filtering a cutout silhouette creates intermediate
        // alpha, so a formerly-cutout texture can become smooth and must then take BC3.
        var useBc3 = ClassifyAlpha(scaled) == AlphaKind.Smooth;

        var levels = new List<byte[]>();
        var cur = scaled;
        var cw = nw;
        var ch = nh;
        while (true)
        {
            levels.Add(useBc3
                ? Bc3Encoder.EncodeImage(cur, cw, ch)
                : Bc1Encoder.EncodeImage(cur, cw, ch));
            if (cw == 1 && ch == 1) break;
            (cur, cw, ch) = Downscale(cur, cw, ch);
        }

        var result = BuildDx10Dds(levels, nw, nh, useBc3 ? 77u : 71u);
        return result.Length < original.Length ? result : null;
    }

    private static AlphaKind ClassifyAlpha(byte[] rgba)
    {
        var sawZero = false;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            var a = rgba[i];
            if (a == 0) sawZero = true;
            else if (a != 255) return AlphaKind.Smooth;
        }
        return sawZero ? AlphaKind.Cutout : AlphaKind.Opaque;
    }

    // Box-filter to half resolution, clamped to 1 so the mip chain always terminates at 1x1
    // even for non-power-of-two sizes (floor-halving alone would reach 0 and loop forever).
    // Each destination pixel averages its exact source span, so odd dimensions keep every row.
    private static (byte[] rgba, int width, int height) Downscale(byte[] src, int w, int h)
    {
        var nw = Math.Max(1, w / 2);
        var nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
        {
            var sy0 = y * h / nh;
            var sy1 = Math.Max(sy0 + 1, (y + 1) * h / nh);
            for (var x = 0; x < nw; x++)
            {
                var sx0 = x * w / nw;
                var sx1 = Math.Max(sx0 + 1, (x + 1) * w / nw);
                int sr = 0, sg = 0, sb = 0, sa = 0;
                var n = (sy1 - sy0) * (sx1 - sx0);
                for (var sy = sy0; sy < sy1; sy++)
                {
                    var rowBase = sy * w * 4;
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var o = rowBase + sx * 4;
                        sr += src[o]; sg += src[o + 1]; sb += src[o + 2]; sa += src[o + 3];
                    }
                }
                var o2 = (y * nw + x) * 4;
                dst[o2] = (byte)(sr / n);
                dst[o2 + 1] = (byte)(sg / n);
                dst[o2 + 2] = (byte)(sb / n);
                dst[o2 + 3] = (byte)(sa / n);
            }
        }
        return (dst, nw, nh);
    }

    private static int BlockBytes(int w, int h, int blockSize) => ((w + 3) / 4) * ((h + 3) / 4) * blockSize;

    private static int MipChainBytes(int w, int h, int mips, int blockSize)
    {
        var total = 0;
        for (var m = 0; m < mips; m++)
        {
            total += BlockBytes(w, h, blockSize);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return total;
    }

    private static byte[] BuildDx10Dds(List<byte[]> levels, int width, int height, uint dxgiFormat)
    {
        var mips = levels.Count;
        var blockSize = dxgiFormat == 77 ? 16 : 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0x20534444u); // "DDS "
        writer.Write(124u);        // header size
        var flags = 0x00081007u | (mips > 1 ? 0x00020000u : 0u); // CAPS|H|W|PIXFMT|LINEARSIZE[|MIPMAPCOUNT]
        writer.Write(flags);
        writer.Write((uint)height);
        writer.Write((uint)width);
        writer.Write((uint)BlockBytes(width, height, blockSize)); // pitch / linear size of mip0
        writer.Write(0u);          // depth
        writer.Write((uint)mips);
        for (var i = 0; i < 11; i++)
            writer.Write(0u);      // reserved

        writer.Write(32u);         // pixel format size
        writer.Write(0x4u);        // DDPF_FOURCC
        writer.Write(Encoding.ASCII.GetBytes("DX10"));
        writer.Write(0u);          // rgb bit count
        writer.Write(0u);          // r mask
        writer.Write(0u);          // g mask
        writer.Write(0u);          // b mask
        writer.Write(0u);          // a mask

        var caps = 0x1000u | (mips > 1 ? 0x00400008u : 0u); // TEXTURE[|COMPLEX|MIPMAP]
        writer.Write(caps);
        writer.Write(0u);          // caps2
        writer.Write(0u);          // caps3
        writer.Write(0u);          // caps4
        writer.Write(0u);          // reserved2

        // DX10 extended header
        writer.Write(dxgiFormat);
        writer.Write(3u);          // resourceDimension = TEXTURE2D
        writer.Write(0u);          // miscFlag
        writer.Write(1u);          // arraySize
        writer.Write(0u);          // miscFlags2 (alpha mode unknown)

        foreach (var level in levels)
            writer.Write(level);

        writer.Flush();
        return stream.ToArray();
    }
}
