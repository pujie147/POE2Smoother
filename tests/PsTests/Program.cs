using System.Text;
using PsCore;
using PsRules;
using PsTests;

var tests = 0;
var failures = 0;

void Check(string name, bool ok, string? detail = null)
{
    tests++;
    if (ok)
    {
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}{(detail == null ? "" : ": " + detail)}");
    }
}

void Throws<T>(string name, Action action) where T : Exception
{
    try
    {
        action();
        Check(name, false, $"expected {typeof(T).Name}");
    }
    catch (T)
    {
        Check(name, true);
    }
    catch (Exception ex)
    {
        Check(name, false, $"expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
    }
}

// ---------------------------------------------------------------------------
// IgnoreList
// ---------------------------------------------------------------------------
var ignore = IgnoreList.Default();
Check("ignore: monsters are protected", ignore.IsIgnored("metadata/monsters/foo/bar.ot"));
Check("ignore: player skill is not protected", !ignore.IsIgnored("metadata/effects/spells/fire/fireball.ao"));
Check("ignore: prefix directory match", IgnoreList.FromLines(["metadata/effects/x"]).IsIgnored("metadata/effects/x/a.ao"));
Check("ignore: unrelated prefix does not match", !IgnoreList.FromLines(["metadata/effects/x"]).IsIgnored("metadata/effects/y/a.ao"));
Check("ignore: comments are skipped", IgnoreList.FromLines(["# comment", "", "metadata/effects/z"]).Prefixes.Count == 1);

// ---------------------------------------------------------------------------
// PresetSelector
// ---------------------------------------------------------------------------
Check("preset: skill.* matches skill.fire", PresetSelector.Matches("skill.fire", "skill.*"));
Check("preset: skill.* does not match skills", !PresetSelector.Matches("skills", "skill.*"));
Check("preset: * matches anything", PresetSelector.Matches("league.delirium", "*"));
Check("preset: exact match", PresetSelector.Matches("mtx.spells", "mtx.spells"));

// ---------------------------------------------------------------------------
// ReplacementFactory
// ---------------------------------------------------------------------------
var aoText = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\n" +
             "AddEffectPack( 'metadata/effects/spells/fire/burn.epk' );\nKeepThis();\n";
var stripped = ReplacementFactory.Create(Encoding.UTF8.GetBytes(aoText),
    "metadata/effects/spells/fire/fireball.ao", StrategyKind.StripAttachments);
var strippedText = Encoding.UTF8.GetString(stripped);
Check("replace: AddAttached removed", !strippedText.Contains("AddAttached"));
Check("replace: AddEffectPack removed", !strippedText.Contains("AddEffectPack"));
Check("replace: other statements kept", strippedText.Contains("KeepThis"));

var binary = new byte[] { 0, 1, 2, 3, 4 };
var binaryResult = ReplacementFactory.Create(binary, "metadata/effects/x.ao", StrategyKind.StripAttachments);
Check("replace: binary .ao left untouched", binaryResult.SequenceEqual(binary));

var dds = ReplacementFactory.TransparentDds;
Check("replace: transparent DDS magic", dds.Length >= 4 && dds[0] == (byte)'D' && dds[1] == (byte)'D' &&
                                             dds[2] == (byte)'S' && dds[3] == (byte)' ');
Check("replace: transparent DDS size", dds.Length == 136, $"length={dds.Length}");

// Real PoE2 .ao files are UTF-16LE with a BOM; they must be processed, not skipped.
static byte[] U16(string text)
{
    var bytes = new List<byte> { 0xFF, 0xFE };
    bytes.AddRange(Encoding.Unicode.GetBytes(text));
    return bytes.ToArray();
}

var u16In = U16(aoText);
var u16Out = ReplacementFactory.Create(u16In, "metadata/effects/spells/fire/fireball.ao", StrategyKind.StripAttachments);
Check("replace: utf-16 BOM kept", u16Out.Length >= 2 && u16Out[0] == 0xFF && u16Out[1] == 0xFE);
var u16OutText = Encoding.Unicode.GetString(u16Out, 2, u16Out.Length - 2);
Check("replace: utf-16 AddAttached removed", !u16OutText.Contains("AddAttached") && !u16OutText.Contains("AddEffectPack"));
Check("replace: utf-16 other statements kept", u16OutText.Contains("KeepThis"));

var u16Quiet = U16("version 1\nKeepThis();\n");
Check("replace: utf-16 without attachments returns original bytes",
    ReplacementFactory.Create(u16Quiet, "metadata/effects/spells/fire/quiet.ao", StrategyKind.StripAttachments)
        .SequenceEqual(u16Quiet));

var u16NoBom = Encoding.Unicode.GetBytes(aoText);
Check("replace: BOM-less UTF-16 treated as binary and left untouched",
    ReplacementFactory.Create(u16NoBom, "metadata/effects/spells/fire/nib.ao", StrategyKind.StripAttachments)
        .SequenceEqual(u16NoBom));

// ---------------------------------------------------------------------------
// RulePackBuilder
// ---------------------------------------------------------------------------
var packPaths = new[]
{
    "metadata/effects/spells/fire/fireball.ao",
    "metadata/effects/spells/fire/tex.dds",
    "metadata/effects/spells/monsters_effects/act5/bodyfx.pet",
    "metadata/effects/microtransactions/spells/mtx.ao",
    "metadata/effects/environment/league_delirium/fog.ao",
    "metadata/effects/weather_attachments/league_delirium/guidingfog.ao",
    "metadata/effects/spells/breach/hand.ao",
    "metadata/monsters/someboss/someboss.ot"
};
var (pack, stats) = RulePackBuilder.Build(packPaths, "Test Pack", "test");
Check("rules: scanned all", stats.PathsScanned == packPaths.Length);
Check("rules: .ot is skipped", stats.Skipped == 1);
Check("rules: skill.fire category exists", pack.Categories.Any(c => c.Id == "skill.fire"));
Check("rules: skill.fire has ao+dds", pack.Categories.First(c => c.Id == "skill.fire").Rules.Count == 2);
Check("rules: dds strategy", pack.Categories.First(c => c.Id == "skill.fire").Rules
    .Any(r => r.Path.EndsWith(".dds") && r.Strategy == StrategyKind.TransparentDds));
Check("rules: pet is experimental", pack.Categories.First(c => c.Id == "monsters").Rules
    .Any(r => r.Experimental));
Check("rules: mtx category", pack.Categories.Any(c => c.Id == "mtx.spells"));
Check("rules: delirium mapping", pack.Categories.First(c => c.Id == "league.delirium").Rules.Count == 2);
Check("rules: breach mapping", pack.Categories.Any(c => c.Id == "league.breach"));

var safeIds = pack.SelectedCategories("safe").Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
Check("rules: safe includes skill", safeIds.Contains("skill.fire"));
Check("rules: safe includes mtx", safeIds.Contains("mtx.spells"));
Check("rules: safe excludes monsters", !safeIds.Contains("monsters"));
Check("rules: safe excludes leagues", !safeIds.Contains("league.delirium"));

// ---------------------------------------------------------------------------
// Path helpers
// ---------------------------------------------------------------------------
var tempRoot = TestIndexBuilder.NewRoot("paths");
var bundlesDir = Path.Combine(tempRoot, "Bundles2");
Directory.CreateDirectory(bundlesDir);
var fakeIndex = Path.Combine(bundlesDir, "_.index.bin");
File.WriteAllText(fakeIndex, "not a real index");
var fromIndex = SteamLibraryLocator.FromIndex(fakeIndex);
Check("path: from index -> bundles", fromIndex.BundlesDir == Path.GetFullPath(bundlesDir));
Check("path: from index -> game root", fromIndex.GameRoot == Path.GetFullPath(tempRoot));
var fromRoot = SteamLibraryLocator.FromGameRoot(tempRoot);
Check("path: from game root -> index", Path.GetFullPath(fromRoot.IndexPath) == Path.GetFullPath(fakeIndex));

// ---------------------------------------------------------------------------
// AppService: apply, idempotency, restore
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("apply");
    var bundles = Path.Combine(root, "Bundles2");
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\n" +
                     "AddEffectPack( 'metadata/effects/spells/fire/burn.epk' );\nKeepThis();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes(originalAo)),
        ("metadata/effects/spells/fire/tex.dds", new byte[] { 1, 2, 3, 4 }),
        ("metadata/monsters/foo/foo.ot", new byte[] { 9, 9, 9 })
    });

    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);

    var (pack2, _) = service.BuildRules(game, "Apply Test", "test");
    RulePackStore.Save(pack2, Path.Combine(root, "pack.json"));

    var request = new ApplyRequest
    {
        Game = game,
        Pack = pack2,
        RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        IncludeExperimental = false,
        Force = true
    };

    var result = service.Apply(request);
    Check("apply: two files changed", result.Plan.Changes.Count == 2, $"count={result.Plan.Changes.Count}");
    Check("apply: backup exists", File.Exists(result.BackupPath));
    Check("apply: index hash changed", !result.OriginalSha256.Equals(result.AppliedSha256, StringComparison.OrdinalIgnoreCase));

    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile("metadata/effects/spells/fire/fireball.ao", out var aoRecord);
        var text = Encoding.UTF8.GetString(aoRecord!.Read().ToArray());
        Check("apply: ao stripped", !text.Contains("AddAttached") && !text.Contains("AddEffectPack"));
        Check("apply: ao kept other code", text.Contains("KeepThis"));

        read.TryGetFile("metadata/effects/spells/fire/tex.dds", out var ddsRecord);
        var ddsBytes = ddsRecord!.Read().ToArray();
        Check("apply: dds replaced with transparent", ddsBytes.Length == 136 && ddsBytes[0] == (byte)'D');
    }

    var status = service.GetStatus(game);
    Check("apply: status modified", status.IsModified);
    Check("apply: custom bundles present", status.CustomBundlesExist);

    // Applying again must return to the original baseline first.
    var result2 = service.Apply(request);
    Check("apply: second apply succeeds", result2.Plan.Changes.Count == 2);
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile("metadata/effects/spells/fire/fireball.ao", out var aoRecord);
        var text = Encoding.UTF8.GetString(aoRecord!.Read().ToArray());
        Check("apply: second apply still stripped", !text.Contains("AddAttached"));
    }

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("restore: index restored", restore.RestoredIndex);
    Check("restore: custom bundles cleaned", restore.CleanedCustomBundles);
    Check("restore: status original", !service.GetStatus(game).IsModified);
    Check("restore: state removed", StateStore.Load(appPaths) == null);

    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile("metadata/effects/spells/fire/fireball.ao", out var aoRecord);
        var text = Encoding.UTF8.GetString(aoRecord!.Read().ToArray());
        Check("restore: ao content original", text == originalAo);
    }
}

// ---------------------------------------------------------------------------
// AppService: dry run
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("dryrun");
    var bundles = Path.Combine(root, "Bundles2");
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );"))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (pack3, _) = service.BuildRules(game, "Dry Run", "test");
    var before = HashUtil.Sha256File(indexPath);

    var result = service.Apply(new ApplyRequest
    {
        Game = game,
        Pack = pack3,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true,
        DryRun = true
    });

    Check("dry-run: reports change", result.DryRun && result.Plan.Changes.Count == 1);
    Check("dry-run: index unchanged", HashUtil.Sha256File(indexPath) == before);
    Check("dry-run: no state", StateStore.Load(appPaths) == null);
    Check("dry-run: no custom bundles", !BackupManager.HasCustomBundles(bundles));
}

// ---------------------------------------------------------------------------
// AppService: honest buckets (changed vs unchanged vs skipped experimental)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("buckets");
    var bundles = Path.Combine(root, "Bundles2");
    var withAttachments = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/alpha/fireball.ao", Encoding.UTF8.GetBytes(withAttachments)),
        ("metadata/effects/spells/alpha/halo.ao", U16(withAttachments)),
        ("metadata/effects/spells/alpha/quiet.ao", Encoding.UTF8.GetBytes("version 1\nKeepThis();\n")),
        ("metadata/effects/spells/alpha/burn.pet", new byte[] { 1, 2, 3, 4 })
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (pack5, _) = service.BuildRules(game, "Buckets", "test");

    var result = service.Apply(new ApplyRequest
    {
        Game = game,
        Pack = pack5,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true
    });
    var plan = result.Plan;
    Check("buckets: only real byte changes counted", plan.Changes.Count == 2, $"count={plan.Changes.Count}");
    Check("buckets: no-op .ao lands in Unchanged",
        plan.Unchanged.Count == 1 && plan.Unchanged[0].EndsWith("quiet.ao", StringComparison.OrdinalIgnoreCase));
    Check("buckets: experimental rule counted",
        plan.SkippedExperimental.Count == 1 && plan.SkippedExperimental[0].EndsWith("burn.pet", StringComparison.OrdinalIgnoreCase));
    Check("buckets: backup created for real changes", File.Exists(result.BackupPath));

    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile("metadata/effects/spells/alpha/halo.ao", out var haloRecord);
        var bytes = haloRecord!.Read().ToArray();
        Check("buckets: utf-16 replacement keeps BOM", bytes[0] == 0xFF && bytes[1] == 0xFE);
        var text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        Check("buckets: utf-16 attachments stripped in applied index",
            !text.Contains("AddAttached") && text.Contains("KeepThis"));
    }
}

// ---------------------------------------------------------------------------
// AppService: zero-apply writes nothing (foreign filter / every file unchanged)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("zeroapply");
    var bundles = Path.Combine(root, "Bundles2");
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/alpha/quiet.ao", Encoding.UTF8.GetBytes("version 1\nKeepThis();\n"))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (pack6, _) = service.BuildRules(game, "Zero", "test");
    var before = HashUtil.Sha256File(indexPath);

    var r1 = service.Apply(new ApplyRequest
    {
        Game = game, Pack = pack6, Preset = "safe",
        Ignore = IgnoreList.Default(), Force = true
    });
    Check("zero-apply: all-unchanged produces no changes",
        r1.Plan.Changes.Count == 0 && r1.Plan.Unchanged.Count == 1);
    Check("zero-apply: nothing written",
        r1.BackupPath == "" && StateStore.Load(appPaths) == null &&
        !BackupManager.HasCustomBundles(bundles) && HashUtil.Sha256File(indexPath) == before);

    var r2 = service.Apply(new ApplyRequest
    {
        Game = game, Pack = pack6, Preset = "safe",
        Ignore = IgnoreList.Default(), Force = true,
        CategoryFilter = new HashSet<string>(["fx.spells.fire"], StringComparer.OrdinalIgnoreCase)
    });
    Check("zero-apply: foreign category filter matches nothing",
        r2.Plan.Changes.Count == 0 && HashUtil.Sha256File(indexPath) == before);
}

// ---------------------------------------------------------------------------
// AppService: stale pack — rules missing from the current index
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("stale");
    var bundles = Path.Combine(root, "Bundles2");
    var withAttachments = "AddAttached( 'x.pet', <root> );\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes(withAttachments)),
        ("metadata/effects/spells/fire/gone.ao", Encoding.UTF8.GetBytes(withAttachments))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (pack7, _) = service.BuildRules(game, "Stale", "test");

    // Game update: the index is replaced and lost one of the previously known files.
    TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes(withAttachments))
    });

    var result = service.Apply(new ApplyRequest
    {
        Game = game, Pack = pack7, Preset = "safe",
        Ignore = IgnoreList.Default(), Force = true
    });
    Check("stale: missing rule reported",
        result.Plan.Missing.Count == 1 && result.Plan.Missing[0].EndsWith("gone.ao", StringComparison.OrdinalIgnoreCase));
    Check("stale: existing rule still applied", result.Plan.Changes.Count == 1);
}

// ---------------------------------------------------------------------------
// RulePack model: preset matching edge cases
// ---------------------------------------------------------------------------
{
    var foreign = new RulePack
    {
        Name = "Foreign",
        Categories =
        [
            new RuleCategory
            {
                Id = "fx.spells.fire",
                Group = "misc",
                Rules = [new Rule { Path = "fx/a.ao", Strategy = StrategyKind.StripAttachments }]
            }
        ],
        Presets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["safe"] = ["skill.*", "mtx.*"]
        }
    };
    Check("preset: foreign category ids — safe matches nothing", !foreign.SelectedCategories("safe").Any());
    Check("preset: unknown preset name falls back to all", foreign.SelectedCategories("nope").Count() == 1);
}

// ---------------------------------------------------------------------------
// AppService: game update detection + forced restore
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("update");
    var bundles = Path.Combine(root, "Bundles2");
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );"))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (pack4, _) = service.BuildRules(game, "Update Test", "test");

    var request = new ApplyRequest
    {
        Game = game,
        Pack = pack4,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true
    };
    service.Apply(request);

    // Simulate a game update / external index change.
    File.AppendAllText(indexPath, "x");
    var status = service.GetStatus(game);
    Check("update: detected", status.GameUpdated);
    Throws<InvalidOperationException>("update: restore refuses without force",
        () => service.Restore(new RestoreRequest { Game = game }));

    var forced = service.Restore(new RestoreRequest { Game = game, Force = true });
    Check("update: forced restore works", forced.RestoredIndex);
    Check("update: state cleared", StateStore.Load(appPaths) == null);
}

// ---------------------------------------------------------------------------
// Profiles
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("profile");
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var store = new ProfileStore(appPaths);
    store.Save(new Profile
    {
        Name = "deck-safe",
        Preset = "safe",
        Categories = ["skill.fire"],
        Ignore = ["metadata/monsters"]
    });
    Check("profile: listed", store.List().Contains("deck-safe"));
    var loaded = store.Load("deck-safe");
    Check("profile: loaded", loaded != null && loaded.Categories.Contains("skill.fire"));
    Check("profile: deleted", store.Delete("deck-safe") && !store.List().Contains("deck-safe"));
}

Console.WriteLine();
Console.WriteLine($"{tests - failures}/{tests} tests passed.");
return failures == 0 ? 0 : 1;