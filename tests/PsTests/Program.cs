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
// AppService: game update -> safe per-file restore (--force still overwrites)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("update");
    var bundles = Path.Combine(root, "Bundles2");
    var aoPath = "metadata/effects/spells/fire/fireball.ao";
    var v1Ao = "AddAttached( 'x.pet', <root> );\nKeepThis();\n";
    var v2Ao = "AddAttached( 'x.pet', <root> );\nKeepThis();\nNewPatch();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes(v1Ao)) });
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
    Check("update: per-file fingerprints recorded",
        StateStore.Load(appPaths) is { } st0 && st0.PatchedFiles.Count == 1 &&
        st0.PatchedFiles[0].AppliedSha256.Length == 64);

    // Simulate a game update: index and content bundle are rewritten with new data.
    TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes(v2Ao)) });
    var status = service.GetStatus(game);
    Check("update: detected", status.GameUpdated);

    // Default restore must NOT downgrade: the file's content is no longer ours, so it is skipped.
    var safe = service.Restore(new RestoreRequest { Game = game });
    Check("update: per-file restore chosen", safe.PerFile && !safe.RestoredIndex);
    Check("update: changed file skipped", safe.FilesSkipped == 1 && safe.FilesRestored == 0);
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(aoPath, out var rec);
        Check("update: new content kept (no downgrade)",
            Encoding.UTF8.GetString(rec!.Read().ToArray()) == v2Ao);
    }
    Check("update: state cleared", StateStore.Load(appPaths) == null);
    Check("update: orphan custom bundles cleaned", !BackupManager.HasCustomBundles(bundles));

    // Re-apply works directly on the updated index.
    var reapply = service.Apply(request);
    Check("update: re-apply after update succeeds", reapply.Plan.Changes.Count == 1);

    // --force remains the escape hatch: after a second update it overwrites the whole
    // index with the backup, deliberately downgrading. (Content beyond the index file is
    // undefined here because the synthetic update also rewrote the bundle — same caveat
    // applies to --force on a real install, which is why per-file is the default.)
    var backupBeforeForce = StateStore.Load(appPaths)!.BackupPath;
    TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );\nKeepThis();\nNewerPatch();\n")) });
    var forced = service.Restore(new RestoreRequest { Game = game, Force = true });
    Check("update: forced restore overwrites whole index", forced.RestoredIndex && !forced.PerFile);
    Check("update: forced restore wrote back the exact backup",
        HashUtil.Sha256File(indexPath) == HashUtil.Sha256File(backupBeforeForce));
    Check("update: forced restore clears state", StateStore.Load(appPaths) == null);
}

// ---------------------------------------------------------------------------
// AppService: per-file restore judges each file (restore consistent, skip changed)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("restore-mixed");
    var bundles = Path.Combine(root, "Bundles2");
    var alphaPath = "metadata/effects/spells/alpha/fireball.ao";
    var betaPath = "metadata/effects/spells/alpha/halo.ao";
    var alpha = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    var beta = "version 1\nAddAttached( 'metadata/effects/spells/fire/spark.pet', <root> );\nKeepThis();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (alphaPath, Encoding.UTF8.GetBytes(alpha)),
        (betaPath, Encoding.UTF8.GetBytes(beta))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (packM, _) = service.BuildRules(game, "Mixed", "test");
    service.Apply(new ApplyRequest
    {
        Game = game,
        Pack = packM,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true
    });
    Check("mixed: two files suppressed", StateStore.Load(appPaths) is { PatchedFiles.Count: 2 });

    // An external edit changes beta after our apply; alpha stays exactly as we wrote it.
    using (var work = IndexSession.OpenWorkCopy(indexPath, bundles))
    {
        work.TryGetFile(betaPath, out var rec);
        rec!.Write(Encoding.UTF8.GetBytes("MANUALLY EDITED"));
        work.Save();
        work.Commit();
    }

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("mixed: per-file restore chosen", restore.PerFile);
    Check("mixed: consistent file restored", restore.FilesRestored == 1);
    Check("mixed: changed file skipped",
        restore.FilesSkipped == 1 && restore.SkippedPaths.Contains(betaPath));

    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(alphaPath, out var a);
        Check("mixed: alpha back to original", Encoding.UTF8.GetString(a!.Read().ToArray()) == alpha);
        read.TryGetFile(betaPath, out var b);
        Check("mixed: beta keeps the newer content",
            Encoding.UTF8.GetString(b!.Read().ToArray()) == "MANUALLY EDITED");
        Check("mixed: file count intact", read.Index.Files.Count == 2);
    }
    Check("mixed: state cleared", StateStore.Load(appPaths) == null);
    Check("mixed: custom bundle still referenced by beta is kept", BackupManager.HasCustomBundles(bundles));
}

// ---------------------------------------------------------------------------
// AppService: per-file restore keeps suppression when the original bundle is gone
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("restore-unresolved");
    var bundles = Path.Combine(root, "Bundles2");
    var aoPath = "metadata/effects/spells/alpha/fireball.ao";
    var originalAo = "version 1\nAddAttached( 'x.pet', <root> );\nKeepThis();\n";
    // Regex removal leaves the newline that followed the stripped call.
    var strippedAo = "version 1\n\nKeepThis();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes(originalAo)) });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (packU, _) = service.BuildRules(game, "Unresolved", "test");
    service.Apply(new ApplyRequest
    {
        Game = game,
        Pack = packU,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true
    });

    // Lose the original bundle coordinates, then drift the index while the suppressed
    // content itself is unchanged: redirect is impossible, so the file must stay suppressed.
    var st = StateStore.Load(appPaths)!;
    st.PatchedFiles[0].OriginalBundlePath = "Data/missing.bundle.bin";
    StateStore.Save(appPaths, st);
    TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes(strippedAo)) });

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("unresolved: kept suppressed instead of failing",
        restore.PerFile && restore.FilesUnresolved == 1 && restore.FilesRestored == 0 && restore.FilesSkipped == 0);
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(aoPath, out var rec);
        Check("unresolved: content still readable (not corrupted)",
            Encoding.UTF8.GetString(rec!.Read().ToArray()) == strippedAo);
    }
    var st2 = StateStore.Load(appPaths);
    Check("unresolved: state keeps the remaining entry",
        st2 != null && st2.PatchedFiles.Count == 1 && st2.PatchedFiles[0].Path == aoPath);

    // Once the game files are verified (content becomes the original again), a second
    // restore finds nothing of ours and clears the state.
    TestIndexBuilder.Build(bundles, new[] { (aoPath, Encoding.UTF8.GetBytes(originalAo)) });
    var second = service.Restore(new RestoreRequest { Game = game });
    Check("unresolved: later restore skips the replaced file and clears state",
        second.PerFile && second.FilesSkipped == 1 && StateStore.Load(appPaths) == null);
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

// ---------------------------------------------------------------------------
// Locators: Windows-style library file, standalone Content.ggpk
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("locators");
    var steamRoot = Path.Combine(root, "Steam");
    var libRoot = Path.Combine(root, "Lib2");
    var gameA = Path.Combine(steamRoot, "steamapps", "common", "Path of Exile 2");
    var gameB = Path.Combine(libRoot, "steamapps", "common", "PathOfExile2");
    TestIndexBuilder.Build(Path.Combine(gameA, "Bundles2"),
        [("metadata/effects/spells/fire/a.ao", Encoding.UTF8.GetBytes("a"))]);
    TestIndexBuilder.Build(Path.Combine(gameB, "Bundles2"),
        [("metadata/effects/spells/fire/b.ao", Encoding.UTF8.GetBytes("b"))]);

    // Steam writes library paths with escaped backslashes on Windows.
    var escaped = libRoot.Replace(@"\", @"\\");
    File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
        "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + escaped + "\"\n\t}\n}\n");

    var steamCandidates = SteamLibraryLocator.FindCandidates([steamRoot], []);
    Check("locator: primary steam library found",
        steamCandidates.Any(c => Path.GetFullPath(c.GameRoot) == Path.GetFullPath(gameA)));
    Check("locator: vdf library found",
        steamCandidates.Any(c => Path.GetFullPath(c.GameRoot) == Path.GetFullPath(gameB)),
        string.Join("; ", steamCandidates.Select(c => c.GameRoot)));

    var standaloneRoot = Path.Combine(root, "Standalone");
    var stagingBundles = Path.Combine(root, "standalone-staging");
    TestIndexBuilder.Build(stagingBundles,
        [("metadata/effects/spells/fire/c.ao", Encoding.UTF8.GetBytes("c"))]);
    Directory.CreateDirectory(standaloneRoot);
    var ggpkPath = TestGgpkBuilder.Wrap(stagingBundles, Path.Combine(standaloneRoot, "Content.ggpk"));

    var standalone = StandaloneLocator.FindCandidates([standaloneRoot]);
    Check("locator: standalone found", standalone.Count == 1 && standalone[0].IsGgpk,
        $"count={standalone.Count}");

    var manualGgpk = GameLocator.FromManualPath(ggpkPath);
    Check("locator: manual ggpk path", manualGgpk.IsGgpk &&
                                        manualGgpk.TargetPath == Path.GetFullPath(ggpkPath));
    var manualRoot = GameLocator.FromManualPath(standaloneRoot);
    Check("locator: manual ggpk folder", manualRoot.IsGgpk);
}

// ---------------------------------------------------------------------------
// Standalone client: apply, status and restore inside Content.ggpk
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("ggpk");
    var bundles = Path.Combine(root, "Bundles2");
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes(originalAo)),
        ("metadata/effects/spells/fire/tex.dds", new byte[] { 1, 2, 3, 4 })
    });

    var ggpkPath = TestGgpkBuilder.Wrap(bundles, Path.Combine(root, "Content.ggpk"));
    var game = StandaloneLocator.FromGgpk(ggpkPath);
    var store = TargetStore.For(game);
    var originalHash = store.HashIndex();

    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (ggpkPack, _) = service.BuildRules(game, "GGPK Test", "test");

    var request = new ApplyRequest
    {
        Game = game,
        Pack = ggpkPack,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        Force = true
    };

    var result = service.Apply(request);
    Check("ggpk: two files changed", result.Plan.Changes.Count == 2, $"count={result.Plan.Changes.Count}");
    Check("ggpk: backup exists", File.Exists(result.BackupPath));
    Check("ggpk: index hash changed", !result.OriginalSha256.Equals(result.AppliedSha256,
        StringComparison.OrdinalIgnoreCase));
    Check("ggpk: write journal cleared", !File.Exists(ggpkPath + ".psjournal"));

    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile("metadata/effects/spells/fire/fireball.ao", out var aoRecord);
        var text = Encoding.UTF8.GetString(aoRecord!.Read().ToArray());
        Check("ggpk: ao stripped", !text.Contains("AddAttached"));
        Check("ggpk: ao kept other code", text.Contains("KeepThis"));
    }

    var status = service.GetStatus(game);
    Check("ggpk: status modified", status.IsModified);
    Check("ggpk: custom bundles inside ggpk", status.CustomBundlesExist);

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("ggpk: restore reports index restored", restore.RestoredIndex);
    Check("ggpk: index bytes back to original",
        store.HashIndex().Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    Check("ggpk: custom bundles removed", !store.HasCustomBundles());

    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile("metadata/effects/spells/fire/fireball.ao", out var aoRecord);
        Check("ggpk: original ao content back",
            Encoding.UTF8.GetString(aoRecord!.Read().ToArray()) == originalAo);
    }
}

Console.WriteLine();
Console.WriteLine($"{tests - failures}/{tests} tests passed.");
return failures == 0 ? 0 : 1;