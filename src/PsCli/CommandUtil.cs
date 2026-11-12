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

    public static (string Preset, IgnoreList Ignore, bool IncludeExperimental, IReadOnlySet<string>? Categories)
        ResolveSelection(ParsedArgs args, AppPaths paths)
    {
        var preset = args.Get("preset") ?? "safe";
        var ignore = LoadIgnore(args, paths);
        var includeExperimental = args.HasFlag("include-experimental");
        IReadOnlySet<string>? categories = null;

        var profileName = args.Get("profile");
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var profile = new ProfileStore(paths).Load(profileName)
                          ?? throw new InvalidOperationException($"Profile not found: {profileName}");
            preset = profile.Preset;
            ignore = IgnoreList.FromLines(profile.Ignore);
            includeExperimental = profile.IncludeExperimental;
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

        return (preset, ignore, includeExperimental, categories);
    }

    public static ApplyRequest BuildApplyRequest(ParsedArgs args, AppPaths paths, GameLocation game,
        RulePack pack, string packPath)
    {
        var (preset, ignore, includeExperimental, categories) = ResolveSelection(args, paths);
        return new ApplyRequest
        {
            Game = game,
            Pack = pack,
            RulePackPath = packPath,
            Preset = preset,
            Ignore = ignore,
            IncludeExperimental = includeExperimental,
            Force = args.HasFlag("force"),
            DryRun = args.HasFlag("dry-run"),
            CategoryFilter = categories
        };
    }
}