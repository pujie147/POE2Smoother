using PsCore;
using PsRules;

namespace PsCli;

internal static class CommandUtil
{
    public static AppPaths Paths() => new();

    public static GameLocation ResolveGame(ParsedArgs args)
    {
        var index = args.Get("index");
        if (!string.IsNullOrWhiteSpace(index))
            return Validate(GameLocator.FromManualPath(index));

        var root = args.Get("game");
        if (!string.IsNullOrWhiteSpace(root))
            return Validate(GameLocator.FromManualPath(root));

        var ggpk = args.Get("ggpk");
        if (!string.IsNullOrWhiteSpace(ggpk))
            return Validate(StandaloneLocator.FromGgpk(ggpk));

        var best = GameLocator.FindBest();
        if (best == null)
            throw new InvalidOperationException(
                "PoE2 was not found. Use --index <Bundles2/_.index.bin>, --game <game root> " +
                "or --ggpk <Content.ggpk>.");
        return best;
    }

    private static GameLocation Validate(GameLocation location)
    {
        if (!File.Exists(location.TargetPath))
            throw new FileNotFoundException("PoE2 data file not found", location.TargetPath);
        return location;
    }

    public static RulePack LoadPack(ParsedArgs args, AppPaths paths, GameLocation? game)
    {
        var packPath = args.Get("pack");
        if (!string.IsNullOrWhiteSpace(packPath))
            return RulePackStore.Load(packPath);

        if (File.Exists(paths.DefaultRulePackPath))
            return RulePackStore.Load(paths.DefaultRulePackPath);

        if (game == null)
            throw new InvalidOperationException(
                "No rule pack found. Run `pscli build-rules --index ...` first or pass --pack.");

        var (pack, _) = new AppService(paths).BuildRules(game,
            args.Get("name") ?? "PoE2 Core Effects",
            args.Get("build") ?? "unknown");
        RulePackStore.Save(pack, paths.DefaultRulePackPath);
        return pack;
    }

    public static IgnoreList LoadIgnore(ParsedArgs args, AppPaths paths)
    {
        var ignorePath = args.Get("ignore");
        if (!string.IsNullOrWhiteSpace(ignorePath))
            return IgnoreList.Load(ignorePath);

        if (File.Exists(paths.DefaultIgnorePath))
            return IgnoreList.Load(paths.DefaultIgnorePath);

        var ignore = IgnoreList.Default();
        ignore.Save(paths.DefaultIgnorePath);
        return ignore;
    }

    /// <summary>
    /// Reads the risk ceiling. `--include-experimental` is kept as the legacy alias for the
    /// material tier because that is exactly what the old boolean meant (it gated .mat too).
    /// `--experimental` is more specific and wins when both are present.
    /// </summary>
    public static RiskTier ResolveTier(ParsedArgs args)
    {
        var tier = args.HasFlag("include-experimental") ? RiskTier.Material : RiskTier.Safe;

        var requested = args.Get("experimental");
        if (string.IsNullOrWhiteSpace(requested))
            return tier;

        if (!RiskTiers.TryParse(requested, out var parsed))
            throw new ArgumentException(
                $"unknown --experimental value '{requested}' (safe, off, fx or material)");

        return parsed;
    }

    /// <summary>
    /// Set semantics, not a ceiling: `--undo-tiers material` undoes materials and nothing else,
    /// while apply's `--experimental material` means "materials and everything below".
    /// </summary>
    public static HashSet<RiskTier>? ResolveUndoTiers(ParsedArgs args)
    {
        var raw = args.Get("undo-tiers");
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!RiskTiers.TryParseTierList(raw, out var tiers))
            throw new ArgumentException(
                $"unknown --undo-tiers value in '{raw}' (expected a list of safe, fx, material)");

        return tiers;
    }

    /// <summary>
    /// Which write strategy a command should use. The profile provides the baseline; an explicit
    /// <c>--blank-style</c> wins. For backward compatibility, compression class flags given without
    /// an explicit strategy promote a non-compression baseline to compress-then-template (so old
    /// scripts still compress, and uncompressible files blank instead of being silently dropped);
    /// an explicit non-compression strategy is respected, with a warning that the flags are inert.
    /// </summary>
    public static WriteStrategy ResolveWriteStrategy(ParsedArgs args, Profile? profile)
    {
        var requested = args.Get("blank-style");
        var explicitStrategy = !string.IsNullOrWhiteSpace(requested);

        WriteStrategy strategy;
        if (explicitStrategy)
        {
            if (!WriteStrategies.TryParse(requested, out strategy))
                throw new ArgumentException(
                    $"unknown --blank-style value '{requested}' (template, empty, compress-only, " +
                    "compress-then-template or compress-then-empty)");
        }
        else
        {
            strategy = profile?.EffectiveWriteStrategy() ?? WriteStrategy.Template;
        }

        var hasClassFlags = args.HasFlag("compress-tex") || args.HasFlag("tex-downscale")
            || args.HasFlag("compress-shader") || args.HasFlag("mesh-lod") || args.HasFlag("mesh-decimate");
        if (hasClassFlags && !WriteStrategies.IsCompressionContaining(strategy))
        {
            if (explicitStrategy)
            {
                Console.Error.WriteLine(
                    $"warning: --blank-style {WriteStrategies.ToKey(strategy)} does not compress; " +
                    "compression class flags are ignored. Use compress-only / compress-then-template / compress-then-empty.");
            }
            else
            {
                strategy = WriteStrategy.CompressThenTemplate;
                Console.Error.WriteLine(
                    "note: compression class flags given without --blank-style; using compress-then-template " +
                    "(compress first, blank when impossible).");
            }
        }

        return strategy;
    }

    public static (string Preset, IgnoreList Ignore, RiskTier MaxTier, IReadOnlySet<string>? Categories,
        CompressionOptions Compression, Profile? Profile) ResolveSelection(ParsedArgs args, AppPaths paths)
    {
        var preset = args.Get("preset") ?? "safe";
        var ignore = LoadIgnore(args, paths);
        var maxTier = ResolveTier(args);
        IReadOnlySet<string>? categories = null;
        Profile? profile = null;

        var profileName = args.Get("profile");
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            profile = new ProfileStore(paths).Load(profileName)
                      ?? throw new InvalidOperationException($"Profile not found: {profileName}");
            preset = profile.Preset;
            ignore = IgnoreList.FromLines(profile.Ignore);
            maxTier = profile.EffectiveTier();
            if (profile.Categories.Count > 0)
                categories = new HashSet<string>(profile.Categories, StringComparer.OrdinalIgnoreCase);
        }

        var categoryArg = args.Get("categories");
        if (!string.IsNullOrWhiteSpace(categoryArg))
        {
            categories = categoryArg
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return (preset, ignore, maxTier, categories, ResolveCompression(args, profile), profile);
    }

    /// <summary>
    /// Compression toggles: the saved profile provides the baseline, explicit flags add to it.
    /// Strength is validated here so a bad value fails before any index is opened.
    /// </summary>
    public static CompressionOptions ResolveCompression(ParsedArgs args, Profile? profile)
    {
        var options = (profile?.Compression ?? new CompressionOptions()).Clone();
        if (args.HasFlag("compress-tex"))
            options.Texture = true;
        if (args.HasFlag("tex-downscale"))
        {
            options.Texture = true;
            options.TextureDownscale = true;
        }
        if (args.HasFlag("compress-shader"))
            options.Shader = true;
        if (args.HasFlag("mesh-lod"))
            options.MeshLod = true;
        if (args.HasFlag("mesh-decimate"))
            options.MeshDecimate = true;

        var strength = args.Get("decimate-strength");
        if (!string.IsNullOrWhiteSpace(strength))
        {
            if (!int.TryParse(strength, out var value) || value < 0 || value > 100)
                throw new ArgumentException(
                    $"unknown --decimate-strength value '{strength}' (expected 0-100)");
            options.DecimateStrength = value;
        }

        return options;
    }

    public static ApplyRequest BuildApplyRequest(ParsedArgs args, AppPaths paths, GameLocation game,
        RulePack pack, string packPath)
    {
        var (preset, ignore, maxTier, categories, compression, profile) = ResolveSelection(args, paths);
        return new ApplyRequest
        {
            Game = game,
            Pack = pack,
            RulePackPath = packPath,
            Preset = preset,
            Ignore = ignore,
            MaxTier = maxTier,
            WriteStrategy = ResolveWriteStrategy(args, profile),
            Compression = compression,
            Force = args.HasFlag("force"),
            DryRun = args.HasFlag("dry-run"),
            CategoryFilter = categories
        };
    }
}