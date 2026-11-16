namespace PsRules.Compression;

/// <summary>
/// PoE2 particle-mesh (.fmt, "mLOD") surgery. Two header layouts exist (flag bit 0x0200 @36):
/// layout A keeps the index count @56 / indices @60, layout B @64 / @68. Only layout A is fully
/// understood end-to-end (header + indices + vertices + identity trailer, nothing between);
/// layout B carries extra undecoded sections after the vertices, so both LOD swap and decimation
/// refuse it rather than splicing bytes it cannot account for.
/// </summary>
public static class FmtMesh
{
    /// <summary>
    /// Candidate paths for the next official LOD levels, derived from the file name. Real client
    /// naming is inconsistent (LOD00 / LOD0 / _FF / _BF suffixes), so only unambiguous numeric
    /// lod tokens are considered and a few suffix variants are probed. The caller must still
    /// validate the sibling's geometry before using it.
    /// </summary>
    public static IEnumerable<string> LodSiblingCandidates(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var match = System.Text.RegularExpressions.Regex.Match(
            name, @"(?i)^(?<pre>.*?lod)(?<num>\d+)(?<post>.*)$");
        if (!match.Success)
            yield break;

        var numText = match.Groups["num"].Value;
        if (!int.TryParse(numText, out var level) || level != 0)
            yield break; // only LOD0 sources; "lod01"-style ambiguity is left alone

        var pre = match.Groups["pre"].Value;
        var post = match.Groups["post"].Value;
        // Suffix variants seen in the client: mp_cyl_LOD0_FF -> mp_cyl_LOD1 / mp_cyl_LOD2_FF.
        var posts = new List<string> { post };
        var trimmed = System.Text.RegularExpressions.Regex.Replace(post, @"(?i)_(FF|BF)$", "");
        if (trimmed != post)
            posts.Add(trimmed);
        else if (post.Length == 0)
            posts.Add("_FF");

        foreach (var next in new[] { 1, 2 })
        {
            // Client naming mixes widths: LOD00 siblings may be LOD01 or LOD1. Probe both;
            // every probe is validated by TryLodSwap before it can become a write.
            var nums = new List<string> { next.ToString(new string('0', numText.Length)) };
            var unpadded = next.ToString();
            if (!nums.Contains(unpadded))
                nums.Add(unpadded);
            foreach (var num in nums)
            {
                foreach (var p in posts)
                {
                    var candidate = $"{dir}/{pre}{num}{p}.fmt".ToLowerInvariant();
                    if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                        yield return candidate;
                }
            }
        }
    }

    /// <summary>
    /// LOD swap: the sibling's self-describing header + geometry under the original file's
    /// identity trailer (mesh name + material binding). Both files must be layout A and fully
    /// parse, and the sibling must actually carry fewer triangles.
    /// </summary>
    public static byte[]? TryLodSwap(byte[] original, byte[] sibling)
    {
        var orig = TryParse(original);
        var sib = TryParse(sibling);
        if (orig == null || sib == null || orig.LayoutB || sib.LayoutB)
            return null;
        if (sib.Tris >= orig.Tris)
            return null;

        var trailerLength = original.Length - orig.VertexEnd;
        if (trailerLength <= 0)
            return null;

        var result = new byte[sib.VertexEnd + trailerLength];
        Buffer.BlockCopy(sibling, 0, result, 0, sib.VertexEnd);
        Buffer.BlockCopy(original, orig.VertexEnd, result, sib.VertexEnd, trailerLength);
        return result;
    }

    /// <summary>
    /// Vertex-clustering decimation, layout A only. Positions are snapped to a uniform grid whose
    /// cell size is binary-searched against a strength-derived keep ratio; each cluster keeps the
    /// vertex nearest its centroid and carries that vertex's full stride bytes verbatim (packed
    /// normals/UVs are never interpreted). Degenerate triangles are dropped, header counts are
    /// patched, and the identity trailer plus all other header bytes stay untouched. Billboards
    /// (≤2 tris) and meshes that would not shrink meaningfully are refused.
    /// </summary>
    public static byte[]? TryDecimate(byte[] original, int strength)
    {
        if (strength <= 0)
            return null;
        var g = TryParse(original);
        if (g == null || g.LayoutB || g.Tris <= 2 || g.Verts <= 4)
            return null;

        var positions = new float[g.Verts][];
        for (var i = 0; i < g.Verts; i++)
        {
            var o = g.VertexStart + i * g.Stride;
            positions[i] =
            [
                BitConverter.ToSingle(original, o),
                BitConverter.ToSingle(original, o + 4),
                BitConverter.ToSingle(original, o + 8)
            ];
        }

        var lo = new float[3];
        var hi = new float[3];
        for (var k = 0; k < 3; k++)
        {
            lo[k] = BitConverter.ToSingle(original, 7 + 8 * k);
            hi[k] = BitConverter.ToSingle(original, 11 + 8 * k);
        }
        var diag = MathF.Sqrt((hi[0] - lo[0]) * (hi[0] - lo[0]) +
                              (hi[1] - lo[1]) * (hi[1] - lo[1]) +
                              (hi[2] - lo[2]) * (hi[2] - lo[2]));
        if (diag <= 0 || !float.IsFinite(diag))
            return null;

        var keep = 1.0 - 0.85 * (Math.Clamp(strength, 0, 100) / 100.0);
        var target = Math.Max(4, (int)(g.Verts * keep));

        List<List<int>> Cluster(double cell)
        {
            var index = new Dictionary<(int, int, int), int>();
            var cells = new List<List<int>>();
            for (var i = 0; i < g.Verts; i++)
            {
                var key = ((int)Math.Floor((positions[i][0] - lo[0]) / cell),
                           (int)Math.Floor((positions[i][1] - lo[1]) / cell),
                           (int)Math.Floor((positions[i][2] - lo[2]) / cell));
                if (!index.TryGetValue(key, out var ci))
                {
                    ci = cells.Count;
                    index[key] = ci;
                    cells.Add([]);
                }
                cells[ci].Add(i);
            }
            return cells;
        }

        double loC = diag / 4096, hiC = diag;
        var cells = Cluster(loC);
        if (cells.Count > target)
        {
            for (var it = 0; it < 24; it++)
            {
                var mid = Math.Sqrt(loC * hiC);
                cells = Cluster(mid);
                if (cells.Count > target)
                    loC = mid;
                else
                    hiC = mid;
            }
            cells = Cluster(hiC);
        }

        var remap = new int[g.Verts];
        var reps = new List<int>(cells.Count);
        foreach (var members in cells)
        {
            double cx = 0, cy = 0, cz = 0;
            foreach (var i in members)
            {
                cx += positions[i][0];
                cy += positions[i][1];
                cz += positions[i][2];
            }
            cx /= members.Count;
            cy /= members.Count;
            cz /= members.Count;
            var rep = members[0];
            var best = double.MaxValue;
            foreach (var i in members)
            {
                var dx = positions[i][0] - cx;
                var dy = positions[i][1] - cy;
                var dz = positions[i][2] - cz;
                var d = dx * dx + dy * dy + dz * dz;
                if (d < best)
                {
                    best = d;
                    rep = i;
                }
            }
            var slot = reps.Count;
            reps.Add(rep);
            foreach (var i in members)
                remap[i] = slot;
        }

        var indices = new List<ushort>(g.Tris * 3);
        for (var t = 0; t < g.Tris; t++)
        {
            var o = 60 + 6 * t;
            var a = remap[BitConverter.ToUInt16(original, o)];
            var b = remap[BitConverter.ToUInt16(original, o + 2)];
            var c = remap[BitConverter.ToUInt16(original, o + 4)];
            if (a == b || b == c || a == c)
                continue;
            indices.Add((ushort)a);
            indices.Add((ushort)b);
            indices.Add((ushort)c);
        }

        var newTris = indices.Count / 3;
        // At least a 5% triangle reduction, and never collapse below a quad.
        if (newTris < 2 || newTris * 20 > g.Tris * 19 || reps.Count < 4)
            return null;

        var trailerLength = original.Length - g.VertexEnd;
        var result = new byte[60 + 2 * indices.Count + reps.Count * g.Stride + trailerLength];
        Buffer.BlockCopy(original, 0, result, 0, 60);
        BitConverter.GetBytes((uint)newTris).CopyTo(result, 44);
        BitConverter.GetBytes((uint)reps.Count).CopyTo(result, 48);
        BitConverter.GetBytes((uint)indices.Count).CopyTo(result, 56);
        for (var i = 0; i < indices.Count; i++)
            BitConverter.GetBytes(indices[i]).CopyTo(result, 60 + 2 * i);
        var vstart = 60 + 2 * indices.Count;
        for (var i = 0; i < reps.Count; i++)
            Buffer.BlockCopy(original, g.VertexStart + reps[i] * g.Stride,
                result, vstart + i * g.Stride, g.Stride);
        Buffer.BlockCopy(original, g.VertexEnd, result, vstart + reps.Count * g.Stride, trailerLength);
        return result;
    }

    internal sealed record Geometry(
        int VertexStart,
        int VertexEnd,
        int Stride,
        int Tris,
        int Verts,
        bool LayoutB);

    /// <summary>
    /// Port of the canvas demo's parse_fmt (prep_scene.py), hardened: finite bbox/positions,
    /// in-range indices, no degenerate triangles, stride derived by bounding-box test with 24
    /// (the confirmed particle stride) preferred. Anything ambiguous returns null — the caller
    /// leaves the original untouched rather than guessing.
    /// </summary>
    internal static Geometry? TryParse(byte[] b)
    {
        if (b.Length < 68 || b[31] != (byte)'D' || b[32] != (byte)'O' || b[33] != (byte)'L' || b[34] != (byte)'m')
            return null;

        var flag = BitConverter.ToUInt32(b, 36);
        var tris = (int)BitConverter.ToUInt32(b, 44);
        var verts = (int)BitConverter.ToUInt32(b, 48);
        var layoutB = (flag & 0x0200) != 0;
        int nidx, istart;
        if (layoutB)
        {
            nidx = (int)BitConverter.ToUInt32(b, 64);
            istart = 68;
        }
        else
        {
            nidx = (int)BitConverter.ToUInt32(b, 56);
            istart = 60;
        }

        if (tris <= 0 || verts <= 0 || nidx != 3 * tris)
            return null;
        var vstart = istart + 2 * nidx;
        if (vstart > b.Length)
            return null;

        for (var i = 0; i < nidx; i++)
        {
            if (BitConverter.ToUInt16(b, istart + 2 * i) >= verts)
                return null;
        }
        for (var i = 0; i < nidx; i += 3)
        {
            var a = BitConverter.ToUInt16(b, istart + 2 * i);
            var c = BitConverter.ToUInt16(b, istart + 2 * i + 2);
            var d = BitConverter.ToUInt16(b, istart + 2 * i + 4);
            if (a == c || c == d || a == d)
                return null;
        }

        Span<float> lo = stackalloc float[3];
        Span<float> hi = stackalloc float[3];
        for (var k = 0; k < 3; k++)
        {
            lo[k] = BitConverter.ToSingle(b, 7 + 8 * k);
            hi[k] = BitConverter.ToSingle(b, 11 + 8 * k);
            if (!float.IsFinite(lo[k]) || !float.IsFinite(hi[k]) || hi[k] < lo[k])
                return null;
        }

        var stride = 0;
        var ambiguous = false;
        foreach (var s in new[] { 12, 16, 20, 24, 28, 32, 36 })
        {
            if (vstart + verts * s > b.Length)
                continue;
            var ok = true;
            for (var i = 0; i < verts && ok; i++)
            {
                for (var k = 0; k < 3; k++)
                {
                    var p = BitConverter.ToSingle(b, vstart + i * s + 4 * k);
                    if (!float.IsFinite(p) || p < lo[k] - 1f || p > hi[k] + 1f)
                    {
                        ok = false;
                        break;
                    }
                }
            }
            if (!ok)
                continue;
            if (s == 24)
            {
                stride = 24;
                ambiguous = false;
                break;
            }
            if (stride == 0)
                stride = s;
            else
                ambiguous = true;
        }
        if (stride == 0 || ambiguous)
            return null;

        return new Geometry(vstart, vstart + verts * stride, stride, tris, verts, layoutB);
    }
}
