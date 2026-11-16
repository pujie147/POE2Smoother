using System.Text;
using System.Text.Json;

namespace PsRules.Compression;

/// <summary>
/// .fxgraph simplification by contract-preserving pass-through, not blanking. Version-6 graphs
/// read registers through InputX nodes and write them back through X nodes; the complex math in
/// between (fresnel rims, mask blending, channel switches) is what costs pixel work. For the
/// whitelisted pure-shading masks we keep every Input node, every register node and every
/// custom_parameter declaration (so material bindings still resolve), and rewire each InputX
/// straight to X. Base albedo/emissive survive; the expensive shading math is gone.
///
/// Only graphs inspected end-to-end are whitelisted. Motion graphs (Rotate_Local, Scale_OverLife)
/// and UV-animation masks are deliberately excluded: motion carries direction information
/// (docs/experimental-risk-tiers.md §2). Everything else returns null and stays untouched.
/// </summary>
public static class FxGraphSimplify
{
    private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "mask_fresnelstatic.fxgraph",
        "mask_texturestatic.fxgraph"
    };

    public static byte[]? TrySimplify(byte[] original, string path)
    {
        if (!Whitelist.Contains(System.IO.Path.GetFileName(path)))
            return null;

        bool utf16;
        string text;
        if (original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE)
        {
            utf16 = true;
            text = Encoding.Unicode.GetString(original, 2, original.Length - 2);
        }
        else if (original.Length >= 2 && original[0] == 0xFE && original[1] == 0xFF)
        {
            return null; // big-endian: refuse rather than guess
        }
        else
        {
            utf16 = false;
            text = Encoding.UTF8.GetString(original).TrimStart('﻿');
        }

        Dictionary<string, object?>? root;
        try
        {
            root = JsonSerializer.Deserialize<Dictionary<string, object?>>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        if (root == null)
            return null;
        if (!root.TryGetValue("version", out var verObj) || verObj is not JsonElement ver ||
            ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out var version) || version != 6)
            return null;
        if (!root.TryGetValue("nodes", out var nodesObj) || nodesObj is not JsonElement nodes ||
            nodes.ValueKind != JsonValueKind.Array)
            return null;
        if (!root.TryGetValue("links", out var linksObj) || linksObj is not JsonElement links ||
            links.ValueKind != JsonValueKind.Array)
            return null;

        var kept = new List<Dictionary<string, object?>>();
        var inputs = new List<(string Type, int Index, string? Stage)>();
        var sinks = new HashSet<(string Type, int Index, string? Stage)>();
        var originalCount = 0;
        foreach (var nodeEl in nodes.EnumerateArray())
        {
            originalCount++;
            if (nodeEl.ValueKind != JsonValueKind.Object)
                return null;
            var node = JsonSerializer.Deserialize<Dictionary<string, object?>>(nodeEl.GetRawText())!;
            if (node.TryGetValue("type", out var typeObj) && typeObj is JsonElement typeEl &&
                typeEl.ValueKind == JsonValueKind.String)
            {
                var type = typeEl.GetString()!;
                var index = node.TryGetValue("index", out var idxObj) && idxObj is JsonElement idxEl &&
                    idxEl.ValueKind == JsonValueKind.Number && idxEl.TryGetInt32(out var ix) ? ix : 0;
                string? stage = node.TryGetValue("stage", out var stObj) && stObj is JsonElement stEl &&
                    stEl.ValueKind == JsonValueKind.String ? stEl.GetString() : null;
                if (type.StartsWith("Input", StringComparison.Ordinal))
                {
                    inputs.Add((type, index, stage));
                    kept.Add(node);
                    continue;
                }
                sinks.Add((type, index, stage));
                // Parameter declarations stay so .mat custom_parameter bindings keep resolving;
                // without their consumer chains they are inert, exactly like after blanking.
                if (node.ContainsKey("custom_parameter"))
                    kept.Add(node);
            }
        }

        var newLinks = new List<Dictionary<string, object?>>();
        var keptSinks = new HashSet<(string, int, string?)>();
        foreach (var (type, index, stage) in inputs)
        {
            var sinkType = type["Input".Length..];
            if (!sinks.Contains((sinkType, index, stage)))
                continue;
            keptSinks.Add((sinkType, index, stage));
            newLinks.Add(new Dictionary<string, object?>
            {
                ["src"] = Endpoint(type, index, stage, "output"),
                ["dst"] = Endpoint(sinkType, index, stage, "input")
            });
        }

        // Keep the register node of every InputX/X pair. A register without an Input counterpart
        // cannot be told apart from intermediate math, so it is dropped and stays unwritten —
        // the same state blanking leaves it in.
        foreach (var nodeEl in nodes.EnumerateArray())
        {
            var node = JsonSerializer.Deserialize<Dictionary<string, object?>>(nodeEl.GetRawText())!;
            if (node.TryGetValue("type", out var typeObj) && typeObj is JsonElement typeEl &&
                typeEl.ValueKind == JsonValueKind.String)
            {
                var type = typeEl.GetString()!;
                if (type.StartsWith("Input", StringComparison.Ordinal) || node.ContainsKey("custom_parameter"))
                    continue;
                var index = node.TryGetValue("index", out var idxObj) && idxObj is JsonElement idxEl &&
                    idxEl.ValueKind == JsonValueKind.Number && idxEl.TryGetInt32(out var ix) ? ix : 0;
                string? stage = node.TryGetValue("stage", out var stObj) && stObj is JsonElement stEl &&
                    stEl.ValueKind == JsonValueKind.String ? stEl.GetString() : null;
                if (inputs.Any(i => i.Type == "Input" + type && i.Index == index && i.Stage == stage))
                    kept.Add(node);
            }
        }

        // Only worth writing when the math actually dominated the graph.
        if (kept.Count * 2 >= originalCount || newLinks.Count == 0)
            return null;

        root["nodes"] = kept;
        root["links"] = newLinks;
        root.Remove("unused_links");
        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        var result = utf16 ? BlankTemplates.U16Le(json) : Encoding.UTF8.GetBytes(json);
        return result.Length < original.Length ? result : null;
    }

    private static Dictionary<string, object?> Endpoint(string type, int index, string? stage, string variable)
    {
        var ep = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["index"] = index,
            ["variable"] = variable
        };
        if (stage != null)
            ep["stage"] = stage;
        return ep;
    }
}
