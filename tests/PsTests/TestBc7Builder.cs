namespace PsTests;

/// <summary>
/// Synthesizes valid BC7 (DXGI 98) DDS fixtures so codec tests need no binary assets. The block
/// builder is a port of the validated Python known-answer builder: constant-color blocks for all
/// eight modes (partition 0, all indices 0) plus a smooth-alpha mode-6 block. Endpoints are exact
/// at 0/255 so a correct decoder must reproduce the constant color texel-for-texel.
/// </summary>
internal static class TestBc7Builder
{
    // modeNum -> (subsets, partitionBits, rotation, indexSelection, colorBits, alphaBits,
    //             endpointPBits, sharedPBits, indexBits, secondaryIndexBits)
    private static readonly (int ns, int npb, bool rot, bool isel, int ncb, int nab, bool epb, bool spb, int nib, int nsib)[] Modes =
    [
        (3, 4, false, false, 4, 0, true, false, 3, 0),  // 1
        (2, 6, false, false, 6, 0, false, true, 3, 0),  // 2
        (3, 6, false, false, 5, 0, false, false, 2, 0), // 3
        (2, 6, false, false, 7, 0, true, false, 2, 0),  // 4
        (1, 0, true, true, 5, 6, false, false, 2, 3),   // 5
        (1, 0, true, false, 7, 8, false, false, 2, 2),  // 6
        (1, 0, false, false, 7, 7, true, false, 4, 0),  // 7
        (2, 6, false, false, 5, 5, true, false, 2, 0),  // 8
    ];

    private sealed class BitWriter
    {
        private readonly byte[] _b = new byte[16];
        private int _pos;
        public void Write(int n, int val)
        {
            for (var i = 0; i < n; i++)
            {
                if (((val >> i) & 1) != 0)
                    _b[_pos >> 3] |= (byte)(1 << (_pos & 7));
                _pos++;
            }
        }
        public byte[] Bytes() => _b;
    }

    // Partition 0 anchor texels per subset count (texel 0 is always an anchor).
    private static bool IsAnchor(int ns, int texel) => texel switch
    {
        0 => true,
        15 => ns >= 2,
        3 => ns == 3,
        _ => false,
    };

    /// <summary>A constant-color BC7 block. Channels must be 0 or 255 (exact endpoints).</summary>
    public static byte[] ConstBlock(int modeNum, int r, int g, int b, int a)
    {
        var m = Modes[modeNum - 1];
        var bw = new BitWriter();
        bw.Write(modeNum, 1 << (modeNum - 1));
        if (m.npb > 0) bw.Write(m.npb, 0);
        if (m.rot) bw.Write(2, 0);
        if (m.isel) bw.Write(1, 0);
        foreach (var c in new[] { r, g, b })
        {
            var ep = c == 255 ? (1 << m.ncb) - 1 : 0;
            for (var s = 0; s < m.ns; s++)
                for (var e = 0; e < 2; e++)
                    bw.Write(m.ncb, ep);
        }
        if (m.nab > 0)
        {
            var epa = a == 255 ? (1 << m.nab) - 1 : 0;
            for (var s = 0; s < m.ns; s++)
                for (var e = 0; e < 2; e++)
                    bw.Write(m.nab, epa);
        }
        var pb = r == 255 ? 1 : 0;
        if (m.epb)
            for (var s = 0; s < m.ns; s++)
                for (var e = 0; e < 2; e++)
                    bw.Write(1, pb);
        else if (m.spb)
            for (var s = 0; s < m.ns; s++)
                bw.Write(1, pb);
        for (var t = 0; t < 16; t++)
        {
            var anchor = IsAnchor(m.ns, t);
            bw.Write(m.nib - (anchor ? 1 : 0), 0);
            if (m.nsib > 0)
                bw.Write(m.nsib - (anchor ? 1 : 0), 0);
        }
        return bw.Bytes();
    }

    /// <summary>
    /// A mode-6 block: opaque white color, alpha endpoints 255/0 with a ramping secondary index,
    /// so decoded alpha takes intermediate values (170, 85) — a genuine smooth-alpha texture.
    /// </summary>
    public static byte[] SmoothAlphaBlock()
    {
        var bw = new BitWriter();
        bw.Write(6, 1 << 5);   // mode 6
        bw.Write(2, 0);        // rotation 0
        for (var i = 0; i < 6; i++) bw.Write(7, 127); // R0,R1,G0,G1,B0,B1 = white
        bw.Write(8, 255);      // A0
        bw.Write(8, 0);        // A1
        // primary (color) indices: all 0, anchor texel 0 has 1 bit
        for (var t = 0; t < 16; t++) bw.Write(t == 0 ? 1 : 2, 0);
        // secondary (alpha) indices: ramp 0,1,2,3 -> alpha 255,170,85,0
        for (var t = 0; t < 16; t++) bw.Write(t == 0 ? 1 : 2, t == 0 ? 0 : (t % 3) + 1);
        return bw.Bytes();
    }

    public static byte[] SolidDds(int w, int h, int r, int g, int b, int a,
        int mips = 1, int modeNum = 6, uint dxgi = 98, uint dimension = 3, uint arraySize = 1, uint miscFlag = 0)
        => BuildDds(w, h, mips, ConstBlock(modeNum, r, g, b, a), dxgi, dimension, arraySize, miscFlag);

    public static byte[] SmoothDds(int w, int h, int mips = 1)
        => BuildDds(w, h, mips, SmoothAlphaBlock(), 98, 3, 1, 0);

    private static byte[] BuildDds(int w, int h, int mips, byte[] block,
        uint dxgi, uint dimension, uint arraySize, uint miscFlag)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x20534444u);
        writer.Write(124u);
        writer.Write(0x00081007u | (mips > 1 ? 0x00020000u : 0u));
        writer.Write((uint)h);
        writer.Write((uint)w);
        writer.Write((uint)(((w + 3) / 4) * ((h + 3) / 4) * 16));
        writer.Write(0u);
        writer.Write((uint)mips);
        for (var i = 0; i < 11; i++) writer.Write(0u);
        writer.Write(32u);
        writer.Write(0x4u);
        writer.Write(new byte[] { (byte)'D', (byte)'X', (byte)'1', (byte)'0' });
        for (var i = 0; i < 5; i++) writer.Write(0u);
        writer.Write(0x1000u | (mips > 1 ? 0x00400008u : 0u));
        for (var i = 0; i < 4; i++) writer.Write(0u);
        writer.Write(dxgi);
        writer.Write(dimension);
        writer.Write(miscFlag);
        writer.Write(arraySize);
        writer.Write(0u);

        var cw = w;
        var ch = h;
        for (var m = 0; m < mips; m++)
        {
            var blocks = ((cw + 3) / 4) * ((ch + 3) / 4);
            for (var i = 0; i < blocks; i++) writer.Write(block);
            cw = Math.Max(1, cw / 2);
            ch = Math.Max(1, ch / 2);
        }
        writer.Flush();
        return stream.ToArray();
    }
}
