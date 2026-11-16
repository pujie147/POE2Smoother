namespace PsRules.Compression;

/// <summary>
/// BC7 (BPTC UNORM) block decoder, a faithful port of Mesa's texcompress_bptc_tmp.h (MIT).
/// ffmpeg cannot decode BC7, so this is the reference path: it is cross-validated against an
/// independent Python port of the same source on real client samples, plus per-mode known-answer
/// tests. Only the UNORM (BC7) variant is needed; the HDR float modes are out of scope.
/// </summary>
public static class Bc7Decoder
{
    private readonly record struct Mode(
        int Subsets, int PartitionBits, bool Rotation, bool IndexSelection,
        int ColorBits, int AlphaBits, bool EndpointPBits, bool SharedPBits,
        int IndexBits, int SecondaryIndexBits);

    private static readonly Mode[] Modes =
    [
        new(3, 4, false, false, 4, 0, true,  false, 3, 0), // mode 1
        new(2, 6, false, false, 6, 0, false, true,  3, 0), // mode 2
        new(3, 6, false, false, 5, 0, false, false, 2, 0), // mode 3
        new(2, 6, false, false, 7, 0, true,  false, 2, 0), // mode 4
        new(1, 0, true,  true,  5, 6, false, false, 2, 3), // mode 5
        new(1, 0, true,  false, 7, 8, false, false, 2, 2), // mode 6
        new(1, 0, false, false, 7, 7, true,  false, 4, 0), // mode 7
        new(2, 6, false, false, 5, 5, true,  false, 2, 0), // mode 8
    ];

    // Two-subset partitions: 2 bits per texel packed into a u32 (texel t at bits 2t..2t+1).
    private static readonly uint[] Partition2 =
    [
        0x50505050,0x40404040,0x54545454,0x54505040,0x50404000,0x55545450,0x55545040,0x54504000,
        0x50400000,0x55555450,0x55544000,0x54400000,0x55555440,0x55550000,0x55555500,0x55000000,
        0x55150100,0x00004054,0x15010000,0x00405054,0x00004050,0x15050100,0x05010000,0x40505054,
        0x00404050,0x05010100,0x14141414,0x05141450,0x01155440,0x00555500,0x15014054,0x05414150,
        0x44444444,0x55005500,0x11441144,0x05055050,0x05500550,0x11114444,0x41144114,0x44111144,
        0x15055054,0x01055040,0x05041050,0x05455150,0x14414114,0x50050550,0x41411414,0x00141400,
        0x00041504,0x00105410,0x10541000,0x04150400,0x50410514,0x41051450,0x05415014,0x14054150,
        0x41050514,0x41505014,0x40011554,0x54150140,0x50505500,0x00555050,0x15151010,0x54540404,
    ];

    private static readonly uint[] Partition3 =
    [
        0xaa685050,0x6a5a5040,0x5a5a4200,0x5450a0a8,0xa5a50000,0xa0a05050,0x5555a0a0,0x5a5a5050,
        0xaa550000,0xaa555500,0xaaaa5500,0x90909090,0x94949494,0xa4a4a4a4,0xa9a59450,0x2a0a4250,
        0xa5945040,0x0a425054,0xa5a5a500,0x55a0a0a0,0xa8a85454,0x6a6a4040,0xa4a45000,0x1a1a0500,
        0x0050a4a4,0xaaa59090,0x14696914,0x69691400,0xa08585a0,0xaa821414,0x50a4a450,0x6a5a0200,
        0xa9a58000,0x5090a0a8,0xa8a09050,0x24242424,0x00aa5500,0x24924924,0x24499224,0x50a50a50,
        0x500aa550,0xaaaa4444,0x66660000,0xa5a0a5a0,0x50a050a0,0x69286928,0x44aaaa44,0x66666600,
        0xaa444444,0x54a854a8,0x95809580,0x96969600,0xa85454a8,0x80959580,0xaa141414,0x96960000,
        0xaaaa1414,0xa05050a0,0xa0a5a5a0,0x96000000,0x40804080,0xa9a8a9a8,0xaaaaaa44,0x2a4a5254,
    ];

    // Anchor (fix-up) texels: [0]=2-subset second subset, [1]/[2]=3-subset second/third subset.
    private static readonly byte[][] Anchor =
    [
        new byte[]
        {
            0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,
            0xf,0x2,0x8,0x2,0x2,0x8,0x8,0xf,0x2,0x8,0x2,0x2,0x8,0x8,0x2,0x2,
            0xf,0xf,0x6,0x8,0x2,0x8,0xf,0xf,0x2,0x8,0x2,0x2,0x2,0xf,0xf,0x6,
            0x6,0x2,0x6,0x8,0xf,0xf,0x2,0x2,0xf,0xf,0xf,0xf,0xf,0x2,0x2,0xf,
        },
        new byte[]
        {
            0x3,0x3,0xf,0xf,0x8,0x3,0xf,0xf,0x8,0x8,0x6,0x6,0x6,0x5,0x3,0x3,
            0x3,0x3,0x8,0xf,0x3,0x3,0x6,0xa,0x5,0x8,0x8,0x6,0x8,0x5,0xf,0xf,
            0x8,0xf,0x3,0x5,0x6,0xa,0x8,0xf,0xf,0x3,0xf,0x5,0xf,0xf,0xf,0xf,
            0x3,0xf,0x5,0x5,0x5,0x8,0x5,0xa,0x5,0xa,0x8,0xd,0xf,0xc,0x3,0x3,
        },
        new byte[]
        {
            0xf,0x8,0x8,0x3,0xf,0xf,0x3,0x8,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0x8,
            0xf,0x8,0xf,0x3,0xf,0x8,0xf,0x8,0x3,0xf,0x6,0xa,0xf,0xf,0xa,0x8,
            0xf,0x3,0xf,0xa,0xa,0x8,0x9,0xa,0x6,0xf,0x8,0xf,0x3,0x6,0x6,0x8,
            0xf,0x3,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0xf,0x3,0xf,0xf,0x8,
        },
    ];

    private static readonly byte[] Weight2 = [0, 21, 43, 64];
    private static readonly byte[] Weight3 = [0, 9, 18, 27, 37, 46, 55, 64];
    private static readonly byte[] Weight4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    /// <summary>Decode one 16-byte BC7 block into 64 bytes (16 RGBA texels, row-major).</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        var modeByte = block[0];
        if (modeByte == 0)
        {
            rgba[..64].Clear(); // reserved mode 0 -> transparent black
            return;
        }

        var modeNum = System.Numerics.BitOperations.TrailingZeroCount(modeByte) + 1; // 1..8
        var mode = Modes[modeNum - 1];
        var bitOffset = modeNum;

        var partition = ExtractBits(block, bitOffset, mode.PartitionBits);
        bitOffset += mode.PartitionBits;
        uint subsets = mode.Subsets switch
        {
            1 => 0u,
            2 => Partition2[partition],
            _ => Partition3[partition],
        };

        var rotation = mode.Rotation ? ExtractBits(block, bitOffset, 2) : 0;
        if (mode.Rotation) bitOffset += 2;
        var indexSelection = mode.IndexSelection ? ExtractBits(block, bitOffset, 1) : 0;
        if (mode.IndexSelection) bitOffset += 1;

        Span<byte> endpoints = stackalloc byte[6 * 4];
        bitOffset = ExtractEndpoints(mode, block, bitOffset, endpoints);
        var head = bitOffset;

        for (var texel = 0; texel < 16; texel++)
        {
            var before = AnchorsBefore(mode.Subsets, partition, texel);
            var secondaryOffset = head + 16 * mode.IndexBits - mode.Subsets
                + mode.SecondaryIndexBits * texel - before;
            var primaryOffset = head + mode.IndexBits * texel - before;
            var subset = (int)((subsets >> (texel * 2)) & 3);
            var anchor = IsAnchor(mode.Subsets, partition, texel);

            var primaryBits = mode.IndexBits - (anchor ? 1 : 0);
            var idx0 = ExtractBits(block, primaryOffset, primaryBits);
            var idx1 = 0;
            if (mode.SecondaryIndexBits > 0)
            {
                var secondaryBits = mode.SecondaryIndexBits - (anchor ? 1 : 0);
                idx1 = ExtractBits(block, secondaryOffset, secondaryBits);
            }

            var index = indexSelection == 1 ? idx1 : idx0;
            var colorBits = indexSelection == 1 ? mode.SecondaryIndexBits : mode.IndexBits;
            var o = texel * 4;
            var e0 = subset * 2 * 4;
            var e1 = (subset * 2 + 1) * 4;
            for (var c = 0; c < 3; c++)
                rgba[o + c] = (byte)Interpolate(endpoints[e0 + c], endpoints[e1 + c], index, colorBits);

            // Alpha uses the opposite index from color in the dual-index modes.
            int alphaIndex, alphaBits;
            if (mode.SecondaryIndexBits > 0 && indexSelection == 0)
            {
                alphaIndex = idx1;
                alphaBits = mode.SecondaryIndexBits;
            }
            else
            {
                alphaIndex = idx0;
                alphaBits = mode.IndexBits;
            }
            rgba[o + 3] = (byte)Interpolate(endpoints[e0 + 3], endpoints[e1 + 3], alphaIndex, alphaBits);

            if (rotation != 0)
            {
                var r = rotation - 1;
                (rgba[o + r], rgba[o + 3]) = (rgba[o + 3], rgba[o + r]);
            }
        }
    }

    /// <summary>Decode a full block-linear BC7 mip into a width*height*4 RGBA buffer.</summary>
    public static byte[] DecodeImage(byte[] data, int offset, int width, int height)
    {
        var result = new byte[width * height * 4];
        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        Span<byte> blockRgba = stackalloc byte[64];
        var pos = offset;
        for (var by = 0; by < blockHeight; by++)
        {
            for (var bx = 0; bx < blockWidth; bx++)
            {
                DecodeBlock(data.AsSpan(pos, 16), blockRgba);
                pos += 16;
                for (var ty = 0; ty < 4; ty++)
                {
                    var y = by * 4 + ty;
                    if (y >= height) continue;
                    for (var tx = 0; tx < 4; tx++)
                    {
                        var x = bx * 4 + tx;
                        if (x >= width) continue;
                        var src = (ty * 4 + tx) * 4;
                        var dst = (y * width + x) * 4;
                        result[dst] = blockRgba[src];
                        result[dst + 1] = blockRgba[src + 1];
                        result[dst + 2] = blockRgba[src + 2];
                        result[dst + 3] = blockRgba[src + 3];
                    }
                }
            }
        }
        return result;
    }

    private static int ExtractBits(ReadOnlySpan<byte> block, int offset, int bits)
    {
        var byteIndex = offset / 8;
        var bitIndex = offset % 8;
        var inByte = Math.Min(bits, 8 - bitIndex);
        var result = 0;
        var bit = 0;
        while (true)
        {
            result |= ((block[byteIndex] >> bitIndex) & ((1 << inByte) - 1)) << bit;
            bits -= inByte;
            if (bits <= 0) return result;
            bit += inByte;
            byteIndex++;
            bitIndex = 0;
            inByte = Math.Min(bits, 8);
        }
    }

    private static byte Expand(byte value, int bits) =>
        (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));

    private static int ExtractEndpoints(Mode mode, ReadOnlySpan<byte> block, int bitOffset,
        Span<byte> endpoints)
    {
        for (var c = 0; c < 3; c++)
            for (var s = 0; s < mode.Subsets; s++)
                for (var e = 0; e < 2; e++)
                {
                    endpoints[(s * 2 + e) * 4 + c] = (byte)ExtractBits(block, bitOffset, mode.ColorBits);
                    bitOffset += mode.ColorBits;
                }

        int components;
        if (mode.AlphaBits > 0)
        {
            for (var s = 0; s < mode.Subsets; s++)
                for (var e = 0; e < 2; e++)
                {
                    endpoints[(s * 2 + e) * 4 + 3] = (byte)ExtractBits(block, bitOffset, mode.AlphaBits);
                    bitOffset += mode.AlphaBits;
                }
            components = 4;
        }
        else
        {
            for (var s = 0; s < mode.Subsets; s++)
                for (var e = 0; e < 2; e++)
                    endpoints[(s * 2 + e) * 4 + 3] = 255;
            components = 3;
        }

        if (mode.EndpointPBits)
        {
            for (var s = 0; s < mode.Subsets; s++)
                for (var e = 0; e < 2; e++)
                {
                    var p = ExtractBits(block, bitOffset, 1);
                    bitOffset++;
                    for (var c = 0; c < components; c++)
                    {
                        var i = (s * 2 + e) * 4 + c;
                        endpoints[i] = (byte)((endpoints[i] << 1) | p);
                    }
                }
        }
        else if (mode.SharedPBits)
        {
            for (var s = 0; s < mode.Subsets; s++)
            {
                var p = ExtractBits(block, bitOffset, 1);
                bitOffset++;
                for (var e = 0; e < 2; e++)
                    for (var c = 0; c < components; c++)
                    {
                        var i = (s * 2 + e) * 4 + c;
                        endpoints[i] = (byte)((endpoints[i] << 1) | p);
                    }
            }
        }

        var colorPrecision = mode.ColorBits + (mode.EndpointPBits ? 1 : 0) + (mode.SharedPBits ? 1 : 0);
        var alphaPrecision = mode.AlphaBits + (mode.EndpointPBits ? 1 : 0) + (mode.SharedPBits ? 1 : 0);
        for (var s = 0; s < mode.Subsets; s++)
            for (var e = 0; e < 2; e++)
            {
                var b = (s * 2 + e) * 4;
                for (var c = 0; c < 3; c++)
                    endpoints[b + c] = Expand(endpoints[b + c], colorPrecision);
                if (mode.AlphaBits > 0)
                    endpoints[b + 3] = Expand(endpoints[b + 3], alphaPrecision);
            }

        return bitOffset;
    }

    private static bool IsAnchor(int subsets, int partition, int texel)
    {
        if (texel == 0) return true;
        return subsets switch
        {
            1 => false,
            2 => Anchor[0][partition] == texel,
            _ => Anchor[1][partition] == texel || Anchor[2][partition] == texel,
        };
    }

    private static int AnchorsBefore(int subsets, int partition, int texel)
    {
        if (texel == 0) return 0;
        var count = 1;
        if (subsets == 2)
        {
            if (texel > Anchor[0][partition]) count++;
        }
        else if (subsets == 3)
        {
            if (texel > Anchor[1][partition]) count++;
            if (texel > Anchor[2][partition]) count++;
        }
        return count;
    }

    private static int Interpolate(byte a, byte b, int index, int indexBits)
    {
        var weight = indexBits switch
        {
            2 => Weight2[index],
            3 => Weight3[index],
            _ => Weight4[index],
        };
        return ((64 - weight) * a + weight * b + 32) >> 6;
    }
}
