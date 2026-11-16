namespace PsRules;

/// <summary>
/// Opt-in rendering-side compression. Unlike the blanking strategies these keep the asset's
/// visual role and only make it cheaper to render, so they pass the directionality test in
/// docs/experimental-risk-tiers.md §2 — but they are still experimental until verified in game.
/// Everything defaults to off; a missing settings section must never enable compression.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>Same-resolution texture recompression (BC7 → BC1). Resolution and UVs stay intact.</summary>
    public bool Texture { get; set; }

    /// <summary>Aggressive optional tier: also halve texture resolution. Changes atlas UV cropping,
    /// so it is a separate, explicitly opted-in switch on top of <see cref="Texture"/>.</summary>
    public bool TextureDownscale { get; set; }

    /// <summary>Replace whitelisted complex .fxgraph shader graphs with known-good simple ones.</summary>
    public bool Shader { get; set; }

    /// <summary>Swap a mesh for its official lower LOD sibling when one exists.</summary>
    public bool MeshLod { get; set; }

    /// <summary>Vertex-clustering decimation for meshes without a usable LOD sibling.</summary>
    public bool MeshDecimate { get; set; }

    /// <summary>0–100: how aggressively to decimate. Higher = fewer triangles kept.</summary>
    public int DecimateStrength { get; set; } = 50;

    public bool AnyEnabled => Texture || TextureDownscale || Shader || MeshLod || MeshDecimate;

    public bool TextureEnabled => Texture || TextureDownscale;

    public int ClampedStrength => Math.Clamp(DecimateStrength, 0, 100);

    public CompressionOptions Clone() => new()
    {
        Texture = Texture,
        TextureDownscale = TextureDownscale,
        Shader = Shader,
        MeshLod = MeshLod,
        MeshDecimate = MeshDecimate,
        DecimateStrength = DecimateStrength
    };

    public static bool IsCompressionStrategy(StrategyKind strategy) =>
        strategy is StrategyKind.CompressDds or StrategyKind.SimplifyGraph
            or StrategyKind.SwapMeshLod or StrategyKind.DecimateMesh;

    /// <summary>
    /// Gameplay-critical visuals are never compressed: monster telegraphs carry direction
    /// information (§2 goal function). The blanking IgnoreList already protects metadata/monsters;
    /// compression candidates live in art/ roots it does not cover, so monster-related paths are
    /// guarded here too (monster_particles, monsters_effects, model subsets).
    /// </summary>
    public static bool IsGameplayProtected(string path) =>
        path.Contains("monster", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normal maps must keep their precision: BC1 would wreck lighting directions.</summary>
    public static bool IsNormalMap(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_n", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_normal", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_norm", StringComparison.OrdinalIgnoreCase);
    }
}
