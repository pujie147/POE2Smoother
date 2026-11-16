using LibBundle3.Records;
using PsRules;
using PsRules.Compression;

namespace PsCore;

/// <summary>
/// Enumerates rendering-side compression candidates independently of the rule pack. The pack
/// only covers the three blanking roots; compression targets live mostly under art/ (a full
/// client census: 27,218 .fmt, 150,714 art .dds). Roots are deliberately narrow — effect meshes
/// and particle textures only. Terrain meshes (21,842), items, monsters, minimap and UI art are
/// out of scope: they are gameplay- or readability-critical, not effect overhead.
/// </summary>
public static class CompressionPlanner
{
    private static readonly string[] MeshRoots =
    [
        "art/models/effects/",
        "art/particles/",
        "art/microtransactions/"
    ];

    private static readonly string[] TextureRoots =
    [
        "art/particles/"
    ];

    private static readonly string[] ShaderRoots =
    [
        "metadata/effects/",
        "metadata/terrain/leagues/",
        "metadata/materials/environment/"
    ];

    public static void AddChanges(PatchPlan plan, IndexSession session, ApplyRequest request,
        HashSet<string> seen)
    {
        var options = request.Compression;
        if (!WriteStrategies.IsCompressionContaining(request.WriteStrategy) || !options.AnyEnabled)
            return;

        foreach (var record in session.ParsedFiles())
        {
            var path = record.Path!;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (!IsCandidate(path, options))
                continue;

            var meshCandidate = ext == ".fmt";
            var texCandidate = ext == ".dds";

            if (!seen.Add(path))
                continue;

            // Mesh LOD swap and decimation are both fx-tier; textures are material-tier because
            // they are shared with ordinary rendering, shaders stay fx like .fxgraph blanking.
            var tier = texCandidate ? RiskTier.Material : RiskTier.Fx;
            if (tier > request.MaxTier)
            {
                plan.SkippedExperimental.Add(path);
                if (tier == RiskTier.Material)
                    plan.SkippedMaterialCount++;
                else
                    plan.SkippedFxCount++;
                continue;
            }

            if (request.Ignore.IsIgnored(path) || CompressionOptions.IsGameplayProtected(path))
            {
                plan.Ignored.Add(path);
                continue;
            }

            // Same "already ours" semantics as blanking: our writes live in custom bundles, and
            // Apply rolls back to the baseline before planning, so this only filters previews
            // against a live patched index — it can never hide a real write during Apply.
            if (BackupManager.IsCustomBundlePath(record.BundleRecord?.Path))
            {
                plan.Unchanged.Add(path);
                continue;
            }

            var original = record.Read().ToArray();
            var strategy = StrategyKind.Keep;
            byte[]? replacement = null;

            if (meshCandidate)
            {
                if (options.MeshLod)
                {
                    foreach (var siblingPath in FmtMesh.LodSiblingCandidates(path))
                    {
                        if (!session.TryGetFile(siblingPath, out var sibling) || sibling == null)
                            continue;
                        replacement = FmtMesh.TryLodSwap(original, sibling.Read().ToArray());
                        if (replacement != null)
                        {
                            strategy = StrategyKind.SwapMeshLod;
                            break;
                        }
                    }
                }

                if (replacement == null && options.MeshDecimate)
                {
                    replacement = FmtMesh.TryDecimate(original, options.ClampedStrength);
                    if (replacement != null)
                        strategy = StrategyKind.DecimateMesh;
                }
            }
            else if (texCandidate)
            {
                replacement = DdsCodec.TryCompress(original, options.TextureDownscale);
                if (replacement != null)
                    strategy = StrategyKind.CompressDds;
            }
            else
            {
                replacement = FxGraphSimplify.TrySimplify(original, path);
                if (replacement != null)
                    strategy = StrategyKind.SimplifyGraph;
            }

            if (replacement == null)
            {
                // Not compressible (unknown layout, smooth alpha, no LOD sibling, not whitelisted).
                // CompressThen* falls back to blanking so the file is not silently dropped: .dds and
                // .fxgraph have blanking strategies, .fmt does not (TryGetStrategy -> Keep) so it stays
                // untouched. CompressionOnly never falls back. The blank tier is the strategy's real
                // tier (a .dds stub is Safe even though CompressDds was Material), and is always <=
                // the candidate tier that already passed the gate above.
                if (WriteStrategies.FallsBackToBlank(request.WriteStrategy) &&
                    RulePackBuilder.TryGetStrategy(path, out var blankStrategy) &&
                    blankStrategy != StrategyKind.Keep)
                {
                    var blankTier = RiskTiers.Of(blankStrategy, path);
                    var blankBytes = ReplacementFactory.Create(original, path, blankStrategy, request.BlankStyle);
                    if (blankBytes.AsSpan().SequenceEqual(original))
                    {
                        plan.Unchanged.Add(path);
                        continue;
                    }

                    // Replacement stays null: the write path re-derives the same bytes from the
                    // strategy + derived BlankStyle, exactly like a blanking change.
                    plan.Changes.Add(new PlannedChange(path, blankStrategy, original.LongLength,
                        "compression", blankTier, blankTier > RiskTier.Safe));
                    continue;
                }

                plan.Unsupported.Add(path);
                continue;
            }

            if (replacement.AsSpan().SequenceEqual(original))
            {
                plan.Unchanged.Add(path);
                continue;
            }

            plan.Changes.Add(new PlannedChange(path, strategy, original.LongLength,
                "compression", tier, true, replacement));
        }
    }

    /// <summary>
    /// Whether the compression pass would visit this path: extension + root + the relevant toggle.
    /// Deliberately NOT tier/ignore/gameplay aware — those stay in <see cref="AddChanges"/> so a
    /// monster graph is still reported Ignored rather than reclassified. The blanking loop uses this
    /// to hand exactly the files compression owns to the planner and keep blanking the rest (e.g. a
    /// metadata .dds, which lives outside the texture-compression root).
    /// </summary>
    public static bool IsCandidate(string path, CompressionOptions options)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".fmt" => (options.MeshLod || options.MeshDecimate) && InRoots(path, MeshRoots),
            ".dds" => options.TextureEnabled && InRoots(path, TextureRoots) && !CompressionOptions.IsNormalMap(path),
            ".fxgraph" => options.Shader && InRoots(path, ShaderRoots),
            _ => false
        };
    }

    private static bool InRoots(string path, string[] roots)
    {
        foreach (var root in roots)
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
