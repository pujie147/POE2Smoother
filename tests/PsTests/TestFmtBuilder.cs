using System.Text;

namespace PsTests;

/// <summary>
/// Synthesizes valid mLOD (.fmt) meshes with the exact header offsets the real client uses,
/// so codec tests never need to bundle game assets. Layout A: nidx@56 / indices@60;
/// layout B: nidx@64 / indices@68 (flag bit 0x0200). Vertices are stride-24 (pos + 12 opaque
/// attribute bytes) followed by a UTF-16 identity trailer, matching real layout-A files.
/// </summary>
internal static class TestFmtBuilder
{
    public static byte[] Grid(int n, bool layoutB = false, string name = "test_mesh", int stride = 24)
    {
        var verts = n * n;
        var tris = (n - 1) * (n - 1) * 2;
        var indices = new List<ushort>();
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                var i0 = (ushort)(y * n + x);
                indices.Add(i0);
                indices.Add((ushort)(i0 + n));
                indices.Add((ushort)(i0 + 1));
                indices.Add((ushort)(i0 + 1));
                indices.Add((ushort)(i0 + n));
                indices.Add((ushort)(i0 + n + 1));
            }
        }

        var positions = new float[verts * 3];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var i = (y * n + x) * 3;
                positions[i] = x;
                positions[i + 1] = y;
                positions[i + 2] = 0;
            }
        }

        return Build(tris, verts, indices.ToArray(), positions, layoutB, name, stride);
    }

    public static byte[] Build(int tris, int verts, ushort[] indices, float[] positions,
        bool layoutB = false, string name = "test_mesh", int stride = 24)
    {
        var nidx = indices.Length;
        var istart = layoutB ? 68 : 60;
        var vstart = istart + 2 * nidx;
        var trailer = BuildTrailer(name);
        var b = new byte[vstart + verts * stride + trailer.Length];

        // bbox: min/max per axis at 7..30 (lo,hi pairs)
        float loX = float.MaxValue, loY = float.MaxValue, loZ = float.MaxValue;
        float hiX = float.MinValue, hiY = float.MinValue, hiZ = float.MinValue;
        for (var i = 0; i < verts; i++)
        {
            loX = Math.Min(loX, positions[i * 3]); hiX = Math.Max(hiX, positions[i * 3]);
            loY = Math.Min(loY, positions[i * 3 + 1]); hiY = Math.Max(hiY, positions[i * 3 + 1]);
            loZ = Math.Min(loZ, positions[i * 3 + 2]); hiZ = Math.Max(hiZ, positions[i * 3 + 2]);
        }
        WriteSingle(b, 7, loX); WriteSingle(b, 11, hiX);
        WriteSingle(b, 15, loY); WriteSingle(b, 19, hiY);
        WriteSingle(b, 23, loZ); WriteSingle(b, 27, hiZ);

        b[31] = (byte)'D'; b[32] = (byte)'O'; b[33] = (byte)'L'; b[34] = (byte)'m';
        WriteU32(b, 36, layoutB ? 0x10200u : 0x10100u);
        WriteU32(b, 44, (uint)tris);
        WriteU32(b, 48, (uint)verts);
        if (layoutB)
        {
            WriteU32(b, 52, (uint)(tris / 2));
            WriteU32(b, 64, (uint)nidx);
        }
        else
        {
            WriteU32(b, 56, (uint)nidx);
        }

        for (var i = 0; i < nidx; i++)
            WriteU16(b, istart + 2 * i, indices[i]);
        for (var i = 0; i < verts; i++)
        {
            var o = vstart + i * stride;
            WriteSingle(b, o, positions[i * 3]);
            WriteSingle(b, o + 4, positions[i * 3 + 1]);
            WriteSingle(b, o + 8, positions[i * 3 + 2]);
            for (var k = 12; k < stride; k++)
                b[o + k] = 0x80; // opaque packed attributes, carried verbatim by the codecs
        }
        Buffer.BlockCopy(trailer, 0, b, vstart + verts * stride, trailer.Length);
        return b;
    }

    public static byte[] BuildTrailer(string name)
    {
        var shape = Encoding.Unicode.GetBytes("fixed_" + name + "Shape\0");
        var material = Encoding.Unicode.GetBytes("Art/particles/FXGraphs/general_mesh_particle_testc.mat\0");
        var t = new byte[16 + shape.Length + material.Length];
        WriteU32(t, 8, (uint)shape.Length);
        WriteU32(t, 12, (uint)material.Length);
        Buffer.BlockCopy(shape, 0, t, 16, shape.Length);
        Buffer.BlockCopy(material, 0, t, 16 + shape.Length, material.Length);
        return t;
    }

    private static void WriteU32(byte[] b, int o, uint v) => BitConverter.GetBytes(v).CopyTo(b, o);
    private static void WriteU16(byte[] b, int o, ushort v) => BitConverter.GetBytes(v).CopyTo(b, o);
    private static void WriteSingle(byte[] b, int o, float v) => BitConverter.GetBytes(v).CopyTo(b, o);
}
