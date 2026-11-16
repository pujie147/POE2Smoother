using System.Text;
using PsCore;
using PsRules;
using PsRules.Compression;
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

// Shapes taken from production .ao bodies: double-quoted epk calls, a <lock> second argument,
// and an assignment whose right-hand side is the call. The old single-quote/<root>-only pattern
// matched none of them, and where it did match it left `this.attached = parent_ao.` behind.
var prodAo =
    "\t\t\tthis.attached = parent_ao.AddAttached( 'Metadata/Items/A/shield_Held.ao', <lock>);\r\n" +
    "\t\t\tparent_ao.RemoveAttached( this.attached );\r\n" +
    "\t\t\tparent_ao.RemoveEffectPack( \"Metadata/Effects/Mtx/corrupted_body.epk\" );\r\n" +
    "\t\t\tIfState( body, 1, { parent_ao.AddEffectPack( \"Metadata/Effects/Mtx/corrupted_body.epk\" ); } );\r\n" +
    "\t\t\tCall( update_size );\r\n";
var prodBytes = ReplacementFactory.Create(U16(prodAo), "metadata/effects/mtx/x.ao",
    StrategyKind.StripAttachments);
var prodStripped = Encoding.Unicode.GetString(prodBytes, 2, prodBytes.Length - 2);
Check("replace: double-quoted epk calls are stripped",
    !prodStripped.Contains("RemoveEffectPack") && !prodStripped.Contains("AddEffectPack"), prodStripped);
Check("replace: <lock> second argument is stripped",
    !prodStripped.Contains("AddAttached"), prodStripped);
Check("replace: assignment keeps a valid right-hand side",
    prodStripped.Contains("this.attached = nil;"), prodStripped);
Check("replace: no dangling receiver is left behind",
    !System.Text.RegularExpressions.Regex.IsMatch(prodStripped, @"parent_ao\.\s*[;\r\n]"),
    prodStripped);
Check("replace: paired RemoveAttached is untouched",
    prodStripped.Contains("parent_ao.RemoveAttached( this.attached );"), prodStripped);
Check("replace: unrelated calls and block syntax survive",
    prodStripped.Contains("Call( update_size );") && prodStripped.Contains("IfState( body, 1, {  } );"),
    prodStripped);

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
        Ignore = ["metadata/monsters"],
        IncludeExperimental = true
    });
    Check("profile: listed", store.List().Contains("deck-safe"));
    var loaded = store.Load("deck-safe");
    Check("profile: loaded", loaded != null && loaded.Categories.Contains("skill.fire"));
    // The GUI/CLI re-apply a saved profile with its own experimental tick; losing it would quietly
    // drop every blanking rule the user had saved.
    Check("profile: experimental tick survives the round trip", loaded!.IncludeExperimental);
    store.Save(new Profile { Name = "plain", Preset = "safe", Categories = ["skill.fire"] });
    Check("profile: a profile saved without the tick stays unticked",
        store.Load("plain") is { IncludeExperimental: false });
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

// ---------------------------------------------------------------------------
// AppService: the complete experimental loop — tick "Experimental" → apply → restore
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("experimental-loop");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string ddsPath = "metadata/effects/spells/fire/tex.dds";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    // Every blanking strategy the "Experimental" checkbox gates.
    // Sizes come from BlankAssert.Fixture so no fake original can ever land on a template's length.
    var blanking = new (string Path, byte[] Original)[]
    {
        ("metadata/effects/spells/fire/burn.pet", null!),
        ("metadata/effects/spells/fire/burn.epk", null!),
        ("metadata/effects/spells/fire/shield.mat", null!),
        ("metadata/effects/spells/fire/spark.fxgraph", null!),
        ("metadata/effects/spells/fire/model.aoc", null!)
    }.Select(b => (Path: b.Path, Original: BlankAssert.Fixture(b.Path))).ToArray();
    var fixture = new List<(string Path, byte[] Content)>
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (ddsPath, new byte[] { 1, 2, 3, 4 })
    };
    fixture.AddRange(blanking.Select(b => (b.Path, b.Original)));
    var indexPath = TestIndexBuilder.Build(bundles, fixture);
    var originalHash = HashUtil.Sha256File(indexPath);

    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packE, _) = service.BuildRules(game, "Experimental Loop", "test");

    ApplyRequest Request(bool includeExperimental) => new()
    {
        Game = game,
        Pack = packE,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = includeExperimental ? RiskTier.Material : RiskTier.Safe,
        Force = true
    };

    static byte[] LiveBytes(IndexSession session, string path)
    {
        session.TryGetFile(path, out var record);
        return record!.Read().ToArray();
    }

    // Unticked: each blanking rule must be *named* in the plan, and none may reach the index.
    var offPlan = service.BuildPlan(Request(false));
    Check("exp-off: only stable rules are changes",
        offPlan.Changes.Count == 2 && offPlan.Changes.All(c => !c.Experimental),
        $"count={offPlan.Changes.Count}");
    Check("exp-off: every blanking rule lands in the skip bucket",
        offPlan.SkippedExperimental.Count == blanking.Length &&
        blanking.All(b => offPlan.SkippedExperimental.Contains(b.Path, StringComparer.OrdinalIgnoreCase)));
    Check("exp-off: no blanking rule is silently dropped",
        offPlan.Missing.Count == 0 && offPlan.Unsupported.Count == 0 && offPlan.Ignored.Count == 0);

    // Ticked: the loop must cover exactly the rules the unticked plan skipped — nothing more, nothing less.
    var onPlan = service.BuildPlan(Request(true));
    Check("exp-on: ticking applies stable + skipped rules",
        new HashSet<string>(onPlan.Changes.Select(c => c.Path), StringComparer.OrdinalIgnoreCase).SetEquals(
            offPlan.Changes.Select(c => c.Path).Concat(offPlan.SkippedExperimental)),
        $"on={onPlan.Changes.Count}, off={offPlan.Changes.Count}, skipped={offPlan.SkippedExperimental.Count}");
    Check("exp-on: nothing left in the skip bucket",
        onPlan.SkippedExperimental.Count == 0 &&
        onPlan.Changes.Count(c => c.Experimental) == blanking.Length);

    var offResult = service.Apply(Request(false));
    Check("exp-off: apply writes only the stable rules",
        offResult.Plan.Changes.Count == 2 && offResult.Plan.SkippedExperimental.Count == blanking.Length);
    Check("exp-off: state tracks 2 files", StateStore.Load(appPaths) is { PatchedFiles.Count: 2 });
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("exp-off: .ao stripped", !Encoding.UTF8.GetString(LiveBytes(read, aoPath)).Contains("AddAttached"));
        foreach (var b in blanking)
            Check($"exp-off: {Path.GetFileName(b.Path)} untouched while unticked",
                LiveBytes(read, b.Path).SequenceEqual(b.Original));
    }

    // Tick the box and apply again: the second apply resets to the backup first, so the already
    // stripped .ao is re-planned as a change instead of quietly reporting "0 files".
    var onResult = service.Apply(Request(true));
    Check("exp-on: apply covers every selected rule",
        onResult.Plan.Changes.Count == blanking.Length + 2 &&
        onResult.Plan.Unchanged.Count == 0 &&
        onResult.Plan.SkippedExperimental.Count == 0,
        $"changes={onResult.Plan.Changes.Count}, unchanged={onResult.Plan.Unchanged.Count}");
    Check("exp-on: backup created", File.Exists(onResult.BackupPath));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        foreach (var b in blanking)
            Check($"exp-on: {Path.GetFileName(b.Path)} blanked",
                BlankAssert.IsTemplated(b.Path, LiveBytes(read, b.Path)));
        Check("exp-on: .ao stripped", !Encoding.UTF8.GetString(LiveBytes(read, aoPath)).Contains("AddAttached"));
        Check("exp-on: .dds transparent", LiveBytes(read, ddsPath).Length == 136);
    }

    var state = StateStore.Load(appPaths)!;
    Check("exp-on: state tracks every patched file", state.PatchedFiles.Count == blanking.Length + 2);
    Check("exp-on: blanked files keep the original coordinates they can be redirected to",
        state.PatchedFiles.Count(BlankAssert.MatchesAppliedTemplate) == blanking.Length,
        "the applied fingerprint must be the template, while the redirect target stays the original");
    var applied = service.GetStatus(game);
    Check("exp-on: status modified", applied.IsModified);
    Check("exp-on: custom bundles present", applied.CustomBundlesExist);
    Check("exp-on: backup reachable from the state", applied.BackupExists);

    // Restore has to hand the client back byte-for-byte, blanked files included.
    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("exp-restore: whole index restored", restore.RestoredIndex && !restore.PerFile);
    Check("exp-restore: index identical to the original", HashUtil.Sha256File(indexPath) == originalHash);
    Check("exp-restore: custom bundles cleaned",
        restore.CleanedCustomBundles && !BackupManager.HasCustomBundles(bundles));
    Check("exp-restore: state removed", StateStore.Load(appPaths) == null);
    Check("exp-restore: status original", !service.GetStatus(game).IsModified);
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("exp-restore: .ao content back", Encoding.UTF8.GetString(LiveBytes(read, aoPath)) == originalAo);
        foreach (var b in blanking)
            Check($"exp-restore: {Path.GetFileName(b.Path)} bytes back",
                LiveBytes(read, b.Path).SequenceEqual(b.Original));
    }

    // Untick again after the restore: the loop stays repeatable and the blanking rules stay out.
    var reapply = service.Apply(Request(false));
    Check("exp-reapply: stable rules re-applied, blanking still skipped",
        reapply.Plan.Changes.Count == 2 && reapply.Plan.SkippedExperimental.Count == blanking.Length);
    using (var read = IndexSession.OpenReadOnly(game))
    {
        foreach (var b in blanking)
            Check($"exp-reapply: {Path.GetFileName(b.Path)} still original",
                LiveBytes(read, b.Path).SequenceEqual(b.Original));
    }
    service.Restore(new RestoreRequest { Game = game });
    Check("exp-reapply: second restore is clean too",
        !service.GetStatus(game).IsModified && StateStore.Load(appPaths) == null &&
        HashUtil.Sha256File(indexPath) == originalHash);
}

// ---------------------------------------------------------------------------
// Risk tiers: what the single "experimental" boolean used to bundle together is now three gates
// ---------------------------------------------------------------------------
Check("tier: script surgery and transparent dds are safe",
    RiskTiers.Of(StrategyKind.StripAttachments, "a/b.ao") == RiskTier.Safe &&
    RiskTiers.Of(StrategyKind.TransparentDds, "a/b.dds") == RiskTier.Safe);
Check("tier: blanking an effect resource is fx",
    RiskTiers.Of(StrategyKind.NullPet, "a/b.pet") == RiskTier.Fx &&
    RiskTiers.Of(StrategyKind.NullEffectPack, "a/b.epk") == RiskTier.Fx &&
    RiskTiers.Of(StrategyKind.NullGraph, "a/b.fxgraph") == RiskTier.Fx &&
    RiskTiers.Of(StrategyKind.Empty, "a/b.aoc") == RiskTier.Fx);
Check("tier: blanking a material is its own tier",
    RiskTiers.Of(StrategyKind.NullMaterial, "a/b.mat") == RiskTier.Material);
Check("tier: keep is never gated", RiskTiers.Of(StrategyKind.Keep, "a/b.mat") == RiskTier.Safe);
Check("tier: Auto resolves through the extension",
    RiskTiers.Of(StrategyKind.Auto, "metadata/effects/spells/a/b.mat") == RiskTier.Material &&
    RiskTiers.Of(StrategyKind.Auto, "metadata/effects/spells/a/b.pet") == RiskTier.Fx &&
    RiskTiers.Of(StrategyKind.Auto, "metadata/effects/spells/a/b.ao") == RiskTier.Safe);
Check("tier: Auto on an unsupported extension is not gated",
    RiskTiers.Of(StrategyKind.Auto, "metadata/effects/spells/a/b.ot") == RiskTier.Safe);
Check("tier: off and safe are the same key, junk is rejected",
    RiskTiers.TryParse("off", out var offTier) && offTier == RiskTier.Safe &&
    RiskTiers.TryParse("", out var emptyTier) && emptyTier == RiskTier.Safe &&
    RiskTiers.TryParse("everything", out _) == false);
Check("tier: key round trip",
    RiskTiers.TryParse(RiskTiers.ToKey(RiskTier.Material), out var back) && back == RiskTier.Material);

// A hand-written pack may mark anything experimental, or mark nothing; the flag is a floor, never
// a ceiling, so upgrading cannot start writing files the old build skipped.
Check("tier: an experimental .ao rule stays gated instead of becoming safe",
    new Rule { Path = "metadata/effects/spells/a/b.ao", Strategy = StrategyKind.StripAttachments, Experimental = true }
        .Tier == RiskTier.Fx);
Check("tier: a material rule the old pack forgot to mark is still material",
    new Rule { Path = "metadata/effects/spells/a/b.mat", Strategy = StrategyKind.NullMaterial, Experimental = false }
        .Tier == RiskTier.Material);

// Legacy settings/profile files only carry the boolean, which used to mean "every blanking
// strategy including .mat" — migrating it to fx would quietly stop suppressing materials.
Check("tier: legacy settings migrate to material",
    new AppSettings { IncludeExperimental = true }.EffectiveTier() == RiskTier.Material &&
    new AppSettings().EffectiveTier() == RiskTier.Safe &&
    new Profile { IncludeExperimental = true }.EffectiveTier() == RiskTier.Material);
{
    var migrated = new AppSettings { IncludeExperimental = true };
    migrated.SetTier(RiskTier.Fx);
    Check("tier: setting a tier keeps both the string and the mirrored legacy boolean",
        migrated.ExperimentalTier == "fx" && migrated.IncludeExperimental &&
        migrated.EffectiveTier() == RiskTier.Fx);
    migrated.SetTier(RiskTier.Safe);
    Check("tier: going back to safe clears the mirrored boolean too",
        migrated.ExperimentalTier == "safe" && !migrated.IncludeExperimental);
}

{
    // One fixture, three tiers: the same selection must grow the change set in two distinct steps.
    var root = TestIndexBuilder.NewRoot("risk-tiers");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    const string matPath = "metadata/effects/spells/fire/shield.mat";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    var petBytes = BlankAssert.Fixture(petPath);
    var matBytes = BlankAssert.Fixture(matPath);
    var indexPath = TestIndexBuilder.Build(bundles, new (string, byte[])[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (petPath, petBytes),
        (matPath, matBytes)
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packT, _) = service.BuildRules(game, "Risk Tiers", "test");

    ApplyRequest At(RiskTier tier) => new()
    {
        Game = game, Pack = packT, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = tier, Force = true
    };

    var safePlan = service.BuildPlan(At(RiskTier.Safe));
    Check("tier plan: safe only writes the script",
        safePlan.Changes.Count == 1 && safePlan.Changes[0].Path == aoPath,
        $"count={safePlan.Changes.Count}");
    Check("tier plan: the skip bucket splits by tier",
        safePlan.SkippedExperimental.Count == 2 &&
        safePlan.SkippedFxCount == 1 && safePlan.SkippedMaterialCount == 1,
        $"fx={safePlan.SkippedFxCount} material={safePlan.SkippedMaterialCount}");

    var fxPlan = service.BuildPlan(At(RiskTier.Fx));
    Check("tier plan: fx adds the particle file and nothing else",
        fxPlan.Changes.Count == 2 && fxPlan.Changes.Any(c => c.Path == petPath) &&
        fxPlan.SkippedExperimental.Count == 1 && fxPlan.SkippedMaterialCount == 1);
    Check("tier plan: material totals are what the confirmation dialog reports",
        fxPlan.MaterialFiles == 0 && fxPlan.MaterialBytes == 0 &&
        service.BuildPlan(At(RiskTier.Material)).MaterialFiles == 1);

    // Apply at fx: the material must not merely stay out of the change list, its bytes have to be
    // untouched — that is the entire point of splitting the old checkbox.
    service.Apply(At(RiskTier.Fx));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(petPath, out var petRecord);
        read.TryGetFile(matPath, out var matRecord);
        Check("tier apply: fx blanks the particle file",
            BlankAssert.IsTemplated(petPath, petRecord!.Read().ToArray()));
        Check("tier apply: fx leaves the material bytes alone",
            matRecord!.Read().ToArray().SequenceEqual(matBytes));
    }
    Check("tier apply: the state tracks no material entries",
        StateStore.Load(appPaths) is { } stT &&
        stT.PatchedFiles.All(e => e.Strategy != nameof(StrategyKind.NullMaterial)),
        $"entries={string.Join(",", StateStore.Load(appPaths)?.PatchedFiles.Select(e => e.Strategy) ?? [])}");

    // Applying one tier higher resets to the baseline first, so raising the tier adds files rather
    // than reporting "0 changes" — the case that made the old UI look broken.
    var raised = service.Apply(At(RiskTier.Material));
    Check("tier apply: raising the tier adds the material instead of doing nothing",
        raised.Plan.Changes.Count == 3, $"count={raised.Plan.Changes.Count}");
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(matPath, out var matRecord);
        Check("tier apply: material blanks the material",
            BlankAssert.IsTemplated(matPath, matRecord!.Read().ToArray()));
    }
}

// ---------------------------------------------------------------------------
// Scoped restore: undo one tier, keep the rest suppressed (F)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("scoped-restore");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    const string matPath = "metadata/effects/spells/fire/shield.mat";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    var petBytes = BlankAssert.Fixture(petPath);
    var matBytes = BlankAssert.Fixture(matPath);
    var indexPath = TestIndexBuilder.Build(bundles, new (string, byte[])[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (petPath, petBytes),
        (matPath, matBytes)
    });
    var originalHash = HashUtil.Sha256File(indexPath);
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packS, _) = service.BuildRules(game, "Scoped Restore", "test");

    ApplyRequest At(RiskTier tier) => new()
    {
        Game = game, Pack = packS, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = tier, Force = true
    };
    RestoreRequest ScopedRestore(params RiskTier[] tiers) => new()
    {
        Game = game, UndoTiers = tiers.ToHashSet()
    };

    static byte[] Bytes(IndexSession session, string path)
    {
        session.TryGetFile(path, out var record);
        return record!.Read().ToArray();
    }

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    // A filter naming every tier is the same request as no filter at all, and must keep the
    // whole-index path with its byte-identical result rather than silently downgrading to per-file.
    service.Apply(At(RiskTier.Material));
    Check("undo scope: every tier still takes the whole-index restore",
        service.Restore(ScopedRestore(RiskTier.Safe, RiskTier.Fx, RiskTier.Material)) is
            { RestoredIndex: true, PerFile: false } && HashUtil.Sha256File(indexPath) == originalHash);

    // A filter that leaves something behind must go per-file even though nothing drifted.
    service.Apply(At(RiskTier.Material));
    var scoped = service.Restore(ScopedRestore(RiskTier.Material));
    Check("undo scope: a partial filter forces the per-file branch",
        scoped is { PerFile: true, Scoped: true, FilesRestored: 1 },
        $"perFile={scoped.PerFile} scoped={scoped.Scoped} restored={scoped.FilesRestored}");
    Check("undo scope: only the material was undone",
        scoped.FilesRestored == 1 && scoped.FilesKept == 2,
        $"restored={scoped.FilesRestored} kept={scoped.FilesKept}");
    var statusScoped = service.GetStatus(game);
    Check("undo scope: a partial restore still reads as applied, not as a game update",
        statusScoped is { IsModified: true, GameUpdated: false } && statusScoped.BackupExists,
        $"modified={statusScoped.IsModified} updated={statusScoped.GameUpdated}");
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("undo scope: material bytes back", Bytes(read, matPath).SequenceEqual(matBytes));
        Check("undo scope: particle still blanked", BlankAssert.IsTemplated(petPath, Bytes(read, petPath)));
        Check("undo scope: script still stripped",
            !Encoding.UTF8.GetString(Bytes(read, aoPath)).Contains("AddAttached"));
    }

    // The kept entries have to stay tracked, or the next restore/re-apply cannot see them.
    var keptState = StateStore.Load(appPaths)!;
    Check("undo scope: state keeps exactly the two remaining files",
        keptState.PatchedFiles.Count == 2 && keptState.FileCount == 2,
        $"count={keptState.PatchedFiles.Count} fileCount={keptState.FileCount}");
    Check("undo scope: the kept entries carry their tiers",
        keptState.PatchedFiles.All(e => e.Tier.Length > 0) &&
        keptState.PatchedFiles.Count(e => e.Tier == nameof(RiskTier.Fx)) == 1,
        string.Join(",", keptState.PatchedFiles.Select(e => e.Tier)));
    Check("undo scope: the partial baseline is recorded",
        keptState.PartialRestoreIndexSha256.Length == 64 &&
        keptState.PartialRestoreIndexSha256 == HashUtil.Sha256File(indexPath));
    Check("undo scope: a custom bundle still in use is not swept",
        BackupManager.HasCustomBundles(bundles));

    // Without this baseline the next apply would read the index as "game updated" and --force
    // would then delete the bundles the two kept suppressions still point into.
    var reapply = service.Apply(new ApplyRequest
    {
        Game = game, Pack = packS, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = RiskTier.Fx
    });
    Check("undo scope: re-applying after a partial restore rolls back to the baseline",
        reapply.Plan.Changes.Count == 2 && StateStore.Load(appPaths) is { PatchedFiles.Count: 2 },
        $"changes={reapply.Plan.Changes.Count}");
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("undo scope: the re-applied run left the material alone",
            Bytes(read, matPath).SequenceEqual(matBytes));
    }

    // Whole-index options cannot honour a subset; combining them must fail loudly.
    Check("undo scope: --undo-tiers with --backup is rejected",
        Throws(() => service.Restore(new RestoreRequest
            { Game = game, UndoTiers = ScopedRestore(RiskTier.Material).UndoTiers, BackupPath = keptState.BackupPath })));
    Check("undo scope: --undo-tiers with --clean-only is rejected",
        Throws(() => service.Restore(new RestoreRequest
            { Game = game, UndoTiers = ScopedRestore(RiskTier.Material).UndoTiers, CleanOnly = true })));
    Check("undo scope: --undo-tiers with --force is rejected",
        Throws(() => service.Restore(new RestoreRequest
            { Game = game, UndoTiers = ScopedRestore(RiskTier.Material).UndoTiers, Force = true })));

    // A state file written before tiers existed has no Tier column; the strategy column has to
    // carry the same classification or the filter would silently keep every material suppressed.
    var legacy = StateStore.Load(appPaths)!;
    foreach (var entry in legacy.PatchedFiles) entry.Tier = "";
    StateStore.Save(appPaths, legacy);
    var legacyScoped = service.Restore(ScopedRestore(RiskTier.Fx));
    Check("undo scope: a legacy state file is classified from its strategy",
        legacyScoped.FilesRestored == 1 && legacyScoped.FilesKept == 1,
        $"restored={legacyScoped.FilesRestored} kept={legacyScoped.FilesKept}");

    service.Restore(new RestoreRequest { Game = game });
    Check("undo scope: an unscoped restore finishes the job",
        StateStore.Load(appPaths) == null && HashUtil.Sha256File(indexPath) == originalHash);
}

// ---------------------------------------------------------------------------
// Post-apply verification (H1) and "already blank" previews (H3)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("verify-applied");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string matPath = "metadata/effects/spells/fire/shield.mat";
    var originalAo = "version 1\nAddAttached( 'x.pet', <root> );\nKeepThis();\n";
    var indexPath = TestIndexBuilder.Build(bundles, new (string, byte[])[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (matPath, new byte[] { 7, 7, 7, 7 })
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packV, _) = service.BuildRules(game, "Verify", "test");

    service.Apply(new ApplyRequest
    {
        Game = game, Pack = packV, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = RiskTier.Material, Force = true
    });
    var written = StateStore.Load(appPaths)!.PatchedFiles;

    // The check that runs inside every apply must also pass when called again from the outside.
    var threw = false;
    try { service.VerifyApplied(written, game); } catch (Exception ex) { threw = true; Console.Error.WriteLine(ex.Message); }
    Check("verify: the committed index matches what apply recorded", !threw);

    // PatchedFileEntry is a plain class, so the fault is injected on a freshly deserialized copy —
    // mutating it never touches the state file on disk.
    bool Fails(Action<List<PatchedFileEntry>> mutate)
    {
        try
        {
            var tampered = StateStore.Load(appPaths)!;
            mutate(tampered.PatchedFiles);
            service.VerifyApplied(tampered.PatchedFiles, game);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    Check("verify: a size that does not match is reported",
        Fails(files => files[0].AppliedSize = 999));
    Check("verify: a bundle that does not match is reported",
        Fails(files => files[0].AppliedBundlePath = "Data/nope.bundle.bin"));
    Check("verify: a file missing from the committed index is reported",
        Fails(files => files[0].Path = "metadata/effects/spells/fire/gone.ao"));

    // H3: planning over an already-patched client must not promise to re-write zero bytes over zero.
    var preview = service.BuildPlan(new ApplyRequest
    {
        Game = game, Pack = packV, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = RiskTier.Material
    });
    Check("verify: a preview over the patched client finds nothing left to write",
        preview.Changes.Count == 0 && preview.Unchanged.Count == 2,
        $"changes={preview.Changes.Count} unchanged={preview.Unchanged.Count}");

    // …and the same request against the pristine client still writes everything (the baseline
    // rollback in Apply is what makes that true, so the filter must not hide real work).
    service.Restore(new RestoreRequest { Game = game });
    var fresh = service.BuildPlan(new ApplyRequest
    {
        Game = game, Pack = packV, Preset = "safe",
        Ignore = IgnoreList.Default(), MaxTier = RiskTier.Material
    });
    Check("verify: the same plan on the original client still counts both files",
        fresh.Changes.Count == 2 && fresh.Unchanged.Count == 0,
        $"changes={fresh.Changes.Count}");
}

// ---------------------------------------------------------------------------
// Blanking templates: the false-skip guard, the empty-bytes fallback, the migration
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("blank-templates");
    var bundles = Path.Combine(root, "Bundles2");
    const string matPath = "metadata/effects/spells/fire/shield.mat";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    var templateMat = BlankAssert.TemplateFor(matPath);
    var templatePet = BlankAssert.TemplateFor(petPath);

    // One .mat is an untouched official file that happens to be exactly as long as the material
    // template; the other is already blanked by us (so its content lands in a custom bundle).
    var indexPath = TestIndexBuilder.Build(bundles, new (string, byte[])[]
    {
        (matPath, BlankAssert.Fixture(matPath)),
        (petPath, BlankAssert.Fixture(petPath))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packB, _) = service.BuildRules(game, "Blank Templates", "test");

    ApplyRequest At(WriteStrategy strategy) => new()
    {
        Game = game, Pack = packB, Preset = "safe", Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material, WriteStrategy = strategy, Force = true
    };

    // The templates are the production shapes: a BOM-only effect pack, a bare `0` particle file,
    // and a JSON document declaring just the format's version.
    Check("template: .epk is a BOM and nothing else",
        ReplacementFactory.BlankReplacement(StrategyKind.NullEffectPack, BlankStyle.Template)!
            .SequenceEqual(new byte[] { 0xFF, 0xFE }));
    Check("template: .pet is BOM + \"0\"",
        templatePet.SequenceEqual(new byte[] { 0xFF, 0xFE, 0x30, 0x00 }),
        $"len={templatePet.Length}");
    Check("template: .mat is BOM + valid JSON with its version",
        templateMat.Length == 42 &&
        System.Text.Encoding.Unicode.GetString(templateMat, 2, templateMat.Length - 2)
            .Replace("\r\n", "\n") == "{\n    \"version\": 4\n}",
        $"len={templateMat.Length}");
    Check("template: .fxgraph declares version 3",
        System.Text.Encoding.Unicode.GetString(
            ReplacementFactory.BlankReplacement(StrategyKind.NullGraph, BlankStyle.Template)!,
            2, ReplacementFactory.BlankReplacement(StrategyKind.NullGraph, BlankStyle.Template)!.Length - 2)
            .Replace("\r\n", "\n") == "{\n    \"version\": 3\n}");

    // `--blank-style empty` must still reproduce the pre-v1.3 behaviour exactly.
    Check("template: the empty-bytes fallback still writes zero-length content",
        ReplacementFactory.BlankReplacement(StrategyKind.NullMaterial, BlankStyle.EmptyBytes)!
            .Length == 0);
    service.Apply(At(WriteStrategy.Empty));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("template: empty style blanks to 0 bytes",
            BlankAssert.IsEmptyBytes(read.TryGetFile(matPath, out var m) ? m!.Read().ToArray() : []));
    }

    // Migration: the same patched client, now planned with templates, must see work to do — if the
    // "already blank" check keyed on bytes alone it would call these done and `plan` would lie.
    var migrate = service.BuildPlan(At(WriteStrategy.Template));
    Check("template: a 0-byte patch is not mistaken for a templated one",
        migrate.Changes.Count == 2 && migrate.Unchanged.Count == 0,
        $"changes={migrate.Changes.Count} unchanged={migrate.Unchanged.Count}");

    service.Apply(At(WriteStrategy.Template));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(matPath, out var rec);
        Check("template: upgrading re-blanks with the template",
            BlankAssert.IsTemplated(matPath, rec!.Read().ToArray()));
    }

    // Restore still hands back the original bytes, template or not.
    service.Restore(new RestoreRequest { Game = game });
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(matPath, out var rec);
        Check("template: restore returns the original after a templated blank",
            rec!.Read().ToArray().AsSpan().Slice(0, 8)
                .SequenceEqual(BlankAssert.Fixture(matPath).AsSpan(0, 8)));
    }

    // The false-skip guard: an *official* file whose length equals the template must still be
    // treated as work, because size alone cannot say "we already wrote this".
    var trickRoot = TestIndexBuilder.NewRoot("blank-template-tripwire");
    var trickBundles = Path.Combine(trickRoot, "Bundles2");
    var trickIndex = TestIndexBuilder.Build(trickBundles, new (string, byte[])[]
    {
        (matPath, templateMat),
        (petPath, BlankAssert.Fixture(petPath))
    });
    var trickGame = SteamLibraryLocator.FromIndex(trickIndex);
    var trickPaths = new AppPaths(Path.Combine(trickRoot, "home"));
    trickPaths.Ensure();
    var trickService = new AppService(trickPaths);
    var (packT, _) = trickService.BuildRules(trickGame, "Tripwire", "test");
    var trickPlan = trickService.BuildPlan(new ApplyRequest
    {
        Game = trickGame, Pack = packT, Preset = "safe", Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material
    });
    Check("template: an official file the same length as the template is not skipped",
        trickPlan.Changes.Any(c => c.Path == matPath) &&
        trickPlan.Unchanged.Count == 0,
        $"changes={trickPlan.Changes.Count} unchanged={trickPlan.Unchanged.Count}");
}

// ---------------------------------------------------------------------------
// AppService: per-file restore of an experimental apply (0-byte records redirect back)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("experimental-perfile");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/alpha/fireball.ao";
    const string petPath = "metadata/effects/spells/alpha/burn.pet";
    const string matPath = "metadata/effects/spells/alpha/shield.mat";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/alpha/burn.pet', <root> );\nKeepThis();\n";
    var pet = BlankAssert.Fixture(petPath);
    var mat = BlankAssert.Fixture(matPath);
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (petPath, pet),
        (matPath, mat)
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (packP, _) = service.BuildRules(game, "Experimental Per-File", "test");

    service.Apply(new ApplyRequest
    {
        Game = game, Pack = packP, Preset = "safe", Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material, Force = true
    });
    Check("exp-perfile: three files suppressed", StateStore.Load(appPaths) is { PatchedFiles.Count: 3 });

    // A game update rewrites the blanked .pet; the .ao and the blanked .mat stay as we wrote them.
    var updatedPet = BlankAssert.Updated(2);
    using (var work = IndexSession.OpenWorkCopy(indexPath, bundles))
    {
        work.TryGetFile(petPath, out var rec);
        rec!.Write(updatedPet);
        work.Save();
        work.Commit();
    }

    var restore = service.Restore(new RestoreRequest { Game = game });
    // PerFile is what separates a redirect restore from a whole-index backup overwrite;
    // RestoredIndex now means only "the index file itself was replaced from the backup".
    Check("exp-perfile: per-file restore chosen", restore is { PerFile: true, RestoredIndex: false });
    Check("exp-perfile: files we still own are redirected back", restore.FilesRestored == 2);
    Check("exp-perfile: updated file skipped",
        restore.FilesSkipped == 1 && restore.SkippedPaths.Contains(petPath));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(aoPath, out var ao);
        Check("exp-perfile: .ao original bytes back",
            Encoding.UTF8.GetString(ao!.Read().ToArray()) == originalAo);
        read.TryGetFile(matPath, out var m);
        Check("exp-perfile: blanking redirects to original bytes",
            m!.Read().ToArray().SequenceEqual(mat));
        read.TryGetFile(petPath, out var p);
        Check("exp-perfile: updated .pet keeps the new content", p!.Read().ToArray().SequenceEqual(updatedPet));
    }
    Check("exp-perfile: state cleared", StateStore.Load(appPaths) == null);
}

// ---------------------------------------------------------------------------
// Scoped restore after a game update, and inside Content.ggpk (F)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("scoped-restore-drift");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/alpha/fireball.ao";
    const string petPath = "metadata/effects/spells/alpha/burn.pet";
    const string matPath = "metadata/effects/spells/alpha/shield.mat";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/alpha/burn.pet', <root> );\nKeepThis();\n";
    var pet = BlankAssert.Fixture(petPath);
    var mat = BlankAssert.Fixture(matPath);
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)), (petPath, pet), (matPath, mat)
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    var service = new AppService(appPaths);
    var (packD, _) = service.BuildRules(game, "Scoped Drift", "test");

    service.Apply(new ApplyRequest
    {
        Game = game, Pack = packD, Preset = "safe", Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material, Force = true
    });

    // The update rewrites the blanked .mat with new content; the .ao and .pet stay exactly as we
    // left them. Undoing the material tier must take back what it still owns and forget the file
    // the game replaced — never write the old bytes over it.
    var updatedMat = BlankAssert.Updated(3);
    using (var work = IndexSession.OpenWorkCopy(indexPath, bundles))
    {
        work.TryGetFile(matPath, out var rec);
        rec!.Write(updatedMat);
        work.Save();
        work.Commit();
    }

    var drifted = service.Restore(new RestoreRequest
    {
        Game = game, UndoTiers = new HashSet<RiskTier> { RiskTier.Material }
    });
    Check("undo drift: scoped restore is still per-file",
        drifted is { PerFile: true, Scoped: true, FilesRestored: 0, FilesSkipped: 1 });
    Check("undo drift: the fx tier and the script are left alone",
        drifted.FilesKept == 2, $"kept={drifted.FilesKept}");
    using (var read = IndexSession.OpenReadOnly(game))
    {
        read.TryGetFile(matPath, out var m);
        Check("undo drift: the updated material keeps the new bytes",
            m!.Read().ToArray().SequenceEqual(updatedMat));
        read.TryGetFile(petPath, out var p);
        Check("undo drift: the particle is still blanked",
            BlankAssert.IsTemplated(petPath, p!.Read().ToArray()));
    }
    var driftedState = StateStore.Load(appPaths)!;
    Check("undo drift: the forgotten file is out of the state, the kept ones are not",
        driftedState.PatchedFiles.Count == 2 &&
        driftedState.PatchedFiles.All(e => e.Path != matPath),
        string.Join(",", driftedState.PatchedFiles.Select(e => e.Path)));

    // The same subset logic has to work inside a container, where there is no atomic rename and
    // no orphan sweep: the index Save is claimed to drop emptied custom bundles by itself, and no
    // test had ever checked that.
    var ggRoot = TestIndexBuilder.NewRoot("scoped-restore-ggpk");
    var ggBundles = Path.Combine(ggRoot, "Bundles2");
    TestIndexBuilder.Build(ggBundles, new[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)), (petPath, pet), (matPath, mat)
    });
    var ggpkPath = TestGgpkBuilder.Wrap(ggBundles, Path.Combine(ggRoot, "Content.ggpk"));
    var ggGame = StandaloneLocator.FromGgpk(ggpkPath);
    var ggStore = TargetStore.For(ggGame);
    var ggPaths = new AppPaths(Path.Combine(ggRoot, "home"));
    ggPaths.Ensure();
    var ggService = new AppService(ggPaths);
    var (packH, _) = ggService.BuildRules(ggGame, "GGPK Scoped", "test");

    ggService.Apply(new ApplyRequest
    {
        Game = ggGame, Pack = packH, Preset = "safe", Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material, Force = true
    });
    var ggScoped = ggService.Restore(new RestoreRequest
    {
        Game = ggGame, UndoTiers = new HashSet<RiskTier> { RiskTier.Material }
    });
    Check("undo ggpk: a scoped restore inside the container goes per-file",
        ggScoped is { PerFile: true, Scoped: true, FilesRestored: 1, FilesKept: 2 },
        $"restored={ggScoped.FilesRestored} kept={ggScoped.FilesKept} msg={ggScoped.Message}");
    using (var read = IndexSession.OpenReadOnly(ggGame))
    {
        read.TryGetFile(matPath, out var m);
        Check("undo ggpk: the material redirected back to its original bytes",
            m!.Read().ToArray().SequenceEqual(mat));
        read.TryGetFile(petPath, out var p);
        Check("undo ggpk: the particle is still blanked inside the container",
            BlankAssert.IsTemplated(petPath, p!.Read().ToArray()));
    }
    Check("undo ggpk: the container still carries the kept suppression",
        ggStore.HasCustomBundles() && ggService.GetStatus(ggGame).IsModified);

    // Finishing the job from here must still be possible: an unscoped restore undoes the rest.
    ggService.Restore(new RestoreRequest { Game = ggGame });
    Check("undo ggpk: a later unscoped restore clears what the scoped one kept",
        StateStore.Load(ggPaths) == null && !ggStore.HasCustomBundles() &&
        ggService.GetStatus(ggGame).IsModified == false);
}

// ---------------------------------------------------------------------------
// Standalone client: the same experimental loop inside Content.ggpk
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("ggpk-experimental-loop");
    var bundles = Path.Combine(root, "Bundles2");
    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    const string matPath = "metadata/effects/spells/fire/shield.mat";
    var originalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    var pet = BlankAssert.Fixture(petPath);
    var mat = BlankAssert.Fixture(matPath);
    TestIndexBuilder.Build(bundles, new[]
    {
        (aoPath, Encoding.UTF8.GetBytes(originalAo)),
        (petPath, pet),
        (matPath, mat)
    });

    var ggpkPath = TestGgpkBuilder.Wrap(bundles, Path.Combine(root, "Content.ggpk"));
    var game = StandaloneLocator.FromGgpk(ggpkPath);
    var store = TargetStore.For(game);
    var originalHash = store.HashIndex();

    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packG, _) = service.BuildRules(game, "GGPK Experimental Loop", "test");

    ApplyRequest Request(bool includeExperimental) => new()
    {
        Game = game,
        Pack = packG,
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = includeExperimental ? RiskTier.Material : RiskTier.Safe,
        Force = true
    };

    static byte[] LiveBytes(IndexSession session, string path)
    {
        session.TryGetFile(path, out var record);
        return record!.Read().ToArray();
    }

    var off = service.Apply(Request(false));
    Check("ggpk-exp: unticked writes only the .ao",
        off.Plan.Changes.Count == 1 && off.Plan.SkippedExperimental.Count == 2);
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("ggpk-exp: .pet untouched while unticked", LiveBytes(read, petPath).SequenceEqual(pet));
        Check("ggpk-exp: .mat untouched while unticked", LiveBytes(read, matPath).SequenceEqual(mat));
    }

    var on = service.Apply(Request(true));
    Check("ggpk-exp: ticked blanks both rules",
        on.Plan.Changes.Count == 3 && on.Plan.SkippedExperimental.Count == 0);
    Check("ggpk-exp: write journal cleared", !File.Exists(ggpkPath + ".psjournal"));
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("ggpk-exp: .pet blanked inside the container",
            BlankAssert.IsTemplated(petPath, LiveBytes(read, petPath)));
        Check("ggpk-exp: .mat blanked inside the container",
            BlankAssert.IsTemplated(matPath, LiveBytes(read, matPath)));
        Check("ggpk-exp: .ao stripped inside the container",
            !Encoding.UTF8.GetString(LiveBytes(read, aoPath)).Contains("AddAttached"));
    }
    var appliedStatus = service.GetStatus(game);
    Check("ggpk-exp: status modified", appliedStatus.IsModified);
    Check("ggpk-exp: custom bundles inside the container", appliedStatus.CustomBundlesExist);
    Check("ggpk-exp: backup of the in-container index recorded", appliedStatus.BackupExists);

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("ggpk-exp: restore reports index restored", restore.RestoredIndex);
    Check("ggpk-exp: container index back to original bytes",
        store.HashIndex().Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    Check("ggpk-exp: custom bundles removed", !store.HasCustomBundles());
    Check("ggpk-exp: state removed", StateStore.Load(appPaths) == null);
    using (var read = IndexSession.OpenReadOnly(game))
    {
        Check("ggpk-exp: .pet bytes back", LiveBytes(read, petPath).SequenceEqual(pet));
        Check("ggpk-exp: .mat bytes back", LiveBytes(read, matPath).SequenceEqual(mat));
        Check("ggpk-exp: .ao content back", Encoding.UTF8.GetString(LiveBytes(read, aoPath)) == originalAo);
    }
    Check("ggpk-exp: status original", !service.GetStatus(game).IsModified);
}

// ---------------------------------------------------------------------------
// Rendering compression: options, gating, planner scaffolding (M0)
// ---------------------------------------------------------------------------
{
    var off = new CompressionOptions();
    Check("compression: defaults all off", !off.AnyEnabled && !off.TextureEnabled);
    Check("compression: strength clamped",
        new CompressionOptions { DecimateStrength = 250 }.ClampedStrength == 100 &&
        new CompressionOptions { DecimateStrength = -5 }.ClampedStrength == 0);
    var cloneSrc = new CompressionOptions { Texture = true, DecimateStrength = 70 };
    var clone = cloneSrc.Clone();
    clone.Texture = false;
    Check("compression: clone is independent", cloneSrc.Texture && !clone.Texture && clone.DecimateStrength == 70);

    var texOnly = new CompressionOptions { Texture = true };
    var downOnly = new CompressionOptions { TextureDownscale = true };
    var shaderOnly = new CompressionOptions { Shader = true };
    Check("compression: art particle dds is a texture candidate",
        CompressionPlanner.IsCandidate("art/particles/fire/b.dds", texOnly) &&
        CompressionPlanner.IsCandidate("art/particles/fire/b.dds", downOnly));
    Check("compression: metadata dds is NOT a texture candidate (root-aware; blanking keeps it)",
        !CompressionPlanner.IsCandidate("metadata/effects/spells/fire/b.dds", texOnly));
    Check("compression: fxgraph candidate only under shader roots + shader switch",
        CompressionPlanner.IsCandidate("metadata/effects/spells/fire/b.fxgraph", shaderOnly) &&
        !CompressionPlanner.IsCandidate("metadata/effects/spells/fire/b.fxgraph", texOnly) &&
        !CompressionPlanner.IsCandidate("art/particles/fire/b.fxgraph", shaderOnly));
    Check("compression: fmt candidate only under mesh roots + a mesh switch",
        CompressionPlanner.IsCandidate("art/particles/fire/b.fmt", new CompressionOptions { MeshLod = true }) &&
        !CompressionPlanner.IsCandidate("art/particles/fire/b.fmt", texOnly) &&
        !CompressionPlanner.IsCandidate("metadata/effects/spells/fire/b.fmt", new CompressionOptions { MeshDecimate = true }));
    Check("compression: normal map and mat/pet are never candidates",
        !CompressionPlanner.IsCandidate("art/particles/fire/b_n.dds", texOnly) &&
        !CompressionPlanner.IsCandidate("a/b.mat", shaderOnly) &&
        !CompressionPlanner.IsCandidate("a/b.pet", shaderOnly));
    Check("compression: strategy kinds recognised",
        CompressionOptions.IsCompressionStrategy(StrategyKind.CompressDds) &&
        CompressionOptions.IsCompressionStrategy(StrategyKind.SimplifyGraph) &&
        CompressionOptions.IsCompressionStrategy(StrategyKind.SwapMeshLod) &&
        CompressionOptions.IsCompressionStrategy(StrategyKind.DecimateMesh) &&
        !CompressionOptions.IsCompressionStrategy(StrategyKind.NullGraph) &&
        !CompressionOptions.IsCompressionStrategy(StrategyKind.Auto));
    Check("compression: texture tier is material, mesh/shader tier is fx",
        RiskTiers.Of(StrategyKind.CompressDds, "a.dds") == RiskTier.Material &&
        RiskTiers.Of(StrategyKind.SimplifyGraph, "a.fxgraph") == RiskTier.Fx &&
        RiskTiers.Of(StrategyKind.SwapMeshLod, "a.fmt") == RiskTier.Fx &&
        RiskTiers.Of(StrategyKind.DecimateMesh, "a.fmt") == RiskTier.Fx);
    Check("compression: monster paths guarded case-insensitively",
        CompressionOptions.IsGameplayProtected("art/models/effects/monster_particles/x.fmt") &&
        CompressionOptions.IsGameplayProtected("art/particles/MONSTERS_effects/x.fmt") &&
        !CompressionOptions.IsGameplayProtected("art/particles/fire/x.fmt"));
    Check("compression: normal maps recognised",
        CompressionOptions.IsNormalMap("art/particles/fire/spark_n.dds") &&
        CompressionOptions.IsNormalMap("art/particles/fire/spark_normal.dds") &&
        CompressionOptions.IsNormalMap("art/particles/fire/spark_norm.dds") &&
        !CompressionOptions.IsNormalMap("art/particles/fire/spark.dds"));

    var settings = new AppSettings
    {
        Compression = new CompressionOptions { Texture = true, MeshLod = true, DecimateStrength = 70 }
    };
    var settingsBack = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
        System.Text.Json.JsonSerializer.Serialize(settings))!;
    Check("compression: settings roundtrip",
        settingsBack.EffectiveCompression().Texture && settingsBack.EffectiveCompression().MeshLod &&
        settingsBack.EffectiveCompression().DecimateStrength == 70 &&
        !settingsBack.EffectiveCompression().Shader);
    Check("compression: null settings section means off", !new AppSettings().EffectiveCompression().AnyEnabled);
    var profile = new Profile
    {
        Name = "comp",
        Compression = new CompressionOptions { Shader = true, MeshDecimate = true, DecimateStrength = 30 }
    };
    var profileBack = System.Text.Json.JsonSerializer.Deserialize<Profile>(
        System.Text.Json.JsonSerializer.Serialize(profile))!;
    Check("compression: profile roundtrip",
        profileBack.EffectiveCompression().Shader && profileBack.EffectiveCompression().MeshDecimate &&
        profileBack.EffectiveCompression().DecimateStrength == 30);
    Check("compression: null profile section means off", !new Profile().EffectiveCompression().AnyEnabled);

    // Fixture: blanking-root files plus art/ compression candidates. Codecs are still M0 stubs,
    // so every candidate must land in Unsupported — never silently written, never blanked instead.
    var root = TestIndexBuilder.NewRoot("compression");
    var bundles = Path.Combine(root, "Bundles2");
    var notMesh = new byte[] { 7, 7, 7 };
    var petBytes = new byte[] { 0x10, 0x20, 0x30 };
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );")),
        ("metadata/effects/spells/fire/burn.pet", petBytes),
        ("metadata/effects/spells/fire/glow.fxgraph", Encoding.Unicode.GetBytes("{\"nodes\":[]}")),
        ("metadata/effects/spells/fire/tex.dds", new byte[] { 1, 2, 3, 4 }),
        ("art/particles/fire/spark.fmt", notMesh),
        ("art/particles/fire/spark.dds", new byte[] { 5, 6, 7, 8 }),
        ("art/particles/fire/spark_n.dds", new byte[] { 9, 9 }),
        ("art/models/effects/monster_particles/m.fmt", notMesh),
        ("metadata/monsters/foo/foo.ot", new byte[] { 9, 9, 9 })
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (packC, _) = service.BuildRules(game, "Compression Test", "test");
    RulePackStore.Save(packC, Path.Combine(root, "pack.json"));

    PatchPlan Plan(CompressionOptions c, RiskTier tier, WriteStrategy ws) => service.BuildPlan(new ApplyRequest
    {
        Game = game,
        Pack = packC,
        RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = tier,
        WriteStrategy = ws,
        Compression = c,
        Force = true
    });

    static byte[] LiveBytes(IndexSession session, string path)
    {
        session.TryGetFile(path, out var record);
        return record!.Read().ToArray();
    }

    const string aoPath = "metadata/effects/spells/fire/fireball.ao";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    const string graphPath = "metadata/effects/spells/fire/glow.fxgraph";
    const string metaTex = "metadata/effects/spells/fire/tex.dds";
    const string artTex = "art/particles/fire/spark.dds";
    const string artNorm = "art/particles/fire/spark_n.dds";
    const string artMesh = "art/particles/fire/spark.fmt";
    const string monMesh = "art/models/effects/monster_particles/m.fmt";

    // Template (no compression): blanking is unchanged, art/ is never touched.
    var pOff = Plan(new CompressionOptions(), RiskTier.Material, WriteStrategy.Template);
    Check("write-strategy: template keeps blanking behaviour",
        pOff.CompressionFiles == 0 &&
        pOff.Changes.Any(ch => ch.Path == graphPath) &&
        pOff.Changes.Any(ch => ch.Path == metaTex) &&
        !pOff.Changes.Any(ch => ch.Path.StartsWith("art/")));

    // CompressionOnly: candidates the (real) codecs cannot compress stay Unsupported, and nothing
    // is blanked — not even the safe-tier .ao/.dds baseline.
    var pShaderOnly = Plan(new CompressionOptions { Shader = true }, RiskTier.Fx, WriteStrategy.CompressionOnly);
    Check("compress-only: uncompressible shader candidate is unsupported, never blanked",
        !pShaderOnly.Changes.Any(ch => ch.Path == graphPath) &&
        pShaderOnly.Unsupported.Contains(graphPath));
    Check("compress-only: blanking is fully off (ao/dds/pet untouched)",
        !pShaderOnly.Changes.Any(ch => ch.Path == aoPath) &&
        !pShaderOnly.Changes.Any(ch => ch.Path == metaTex) &&
        !pShaderOnly.Changes.Any(ch => ch.Path == petPath));

    // CompressThenTemplate: the same uncompressible shader falls back to a valid empty graph, while
    // non-candidate files (metadata .dds, .ao) still blank normally.
    var pShaderThen = Plan(new CompressionOptions { Shader = true }, RiskTier.Fx, WriteStrategy.CompressThenTemplate);
    Check("compress-then-template: uncompressible shader falls back to NullGraph blanking",
        pShaderThen.Changes.Any(ch => ch.Path == graphPath && ch.Strategy == StrategyKind.NullGraph) &&
        pShaderThen.CompressionFallbackFiles >= 1 &&
        !pShaderThen.Unsupported.Contains(graphPath));
    Check("compress-then-template: non-candidate dds/ao still blank",
        pShaderThen.Changes.Any(ch => ch.Path == metaTex) &&
        pShaderThen.Changes.Any(ch => ch.Path == aoPath));

    // The root-blind bug fix: a metadata .dds is a blanking root but NOT a texture-compression root,
    // so texture compression must not drop it — it still blanks.
    var pTexThen = Plan(new CompressionOptions { Texture = true }, RiskTier.Material, WriteStrategy.CompressThenTemplate);
    Check("compress-then-template: metadata dds is blanked, not silently dropped (root-aware)",
        pTexThen.Changes.Any(ch => ch.Path == metaTex && ch.Strategy == StrategyKind.TransparentDds));
    Check("compress-then-template: uncompressible art texture falls back to a transparent stub",
        pTexThen.Changes.Any(ch => ch.Path == artTex && ch.Strategy == StrategyKind.TransparentDds));
    Check("compress-then-template: normal map is not a candidate and art root has no blanking rule",
        !pTexThen.Changes.Any(ch => ch.Path == artNorm) &&
        !pTexThen.Unsupported.Contains(artNorm) &&
        !pTexThen.Ignored.Contains(artNorm));

    var pTexOnly = Plan(new CompressionOptions { Texture = true }, RiskTier.Material, WriteStrategy.CompressionOnly);
    Check("compress-only: metadata dds neither blanked nor compressed (in no bucket)",
        !pTexOnly.Changes.Any(ch => ch.Path == metaTex) &&
        !pTexOnly.Unsupported.Contains(metaTex));
    Check("compress-only: art texture candidate is unsupported, normal map is not a candidate",
        pTexOnly.Unsupported.Contains(artTex) &&
        !pTexOnly.Unsupported.Contains(artNorm) &&
        !pTexOnly.Ignored.Contains(artNorm));

    var pMesh = Plan(new CompressionOptions { MeshLod = true, MeshDecimate = true }, RiskTier.Fx, WriteStrategy.CompressionOnly);
    Check("compression: effect mesh is a candidate", pMesh.Unsupported.Contains(artMesh));
    Check("compression: monster mesh is gameplay-ignored", pMesh.Ignored.Contains(monMesh));

    var pSafe = Plan(new CompressionOptions { MeshLod = true, Texture = true }, RiskTier.Safe, WriteStrategy.CompressionOnly);
    Check("compression: safe tier skips mesh and texture candidates",
        pSafe.SkippedExperimental.Contains(artMesh) &&
        pSafe.SkippedExperimental.Contains(artTex) &&
        pSafe.SkippedFxCount >= 1 && pSafe.SkippedMaterialCount >= 1 &&
        pSafe.CompressionFiles == 0);

    // A tier-skipped candidate must NOT fall back to blanking: the tier is a true shared ceiling.
    var pSafeThen = Plan(new CompressionOptions { Texture = true }, RiskTier.Safe, WriteStrategy.CompressThenTemplate);
    Check("compress-then-template: tier-skipped art texture is not fallback-blanked",
        pSafeThen.SkippedExperimental.Contains(artTex) &&
        !pSafeThen.Changes.Any(ch => ch.Path == artTex));
    Check("compress-then-template: safe-tier metadata dds still blanks at safe tier",
        pSafeThen.Changes.Any(ch => ch.Path == metaTex));

    // CompressThenEmpty: the fallback still blanks an uncompressible shader (0 bytes for .fxgraph).
    var pShaderEmpty = Plan(new CompressionOptions { Shader = true }, RiskTier.Fx, WriteStrategy.CompressThenEmpty);
    Check("compress-then-empty: uncompressible shader falls back to blanking",
        pShaderEmpty.Changes.Any(ch => ch.Path == graphPath && ch.Strategy == StrategyKind.NullGraph));

    // Full apply, compress-then-template: real codecs refuse the garbage fixture candidates, .fmt
    // has no template (Unsupported), .dds/.fxgraph fall back to blanking, non-candidates blank, and
    // restore must bring every byte back.
    var originalHash = HashUtil.Sha256File(indexPath);
    var applied = service.Apply(new ApplyRequest
    {
        Game = game,
        Pack = packC,
        RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Material,
        WriteStrategy = WriteStrategy.CompressThenTemplate,
        Compression = new CompressionOptions
        {
            Texture = true, Shader = true, MeshLod = true, MeshDecimate = true, DecimateStrength = 80
        },
        Force = true
    });
    Check("compression: apply with uncompressible candidates never compresses them",
        applied.Plan.CompressionFiles == 0 &&
        applied.Plan.Unsupported.Contains(artMesh));
    Check("compression: no compression strategy ever targets .pet",
        !applied.Plan.Changes.Any(ch => ch.Path.EndsWith(".pet") &&
            CompressionOptions.IsCompressionStrategy(ch.Strategy)));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        Check("compression: mesh without a template stays untouched on disk",
            LiveBytes(read, artMesh).SequenceEqual(notMesh));
        Check("compress-then-template: art texture fallback wrote a transparent stub",
            LiveBytes(read, artTex).SequenceEqual(ReplacementFactory.TransparentDds));
    }
    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("compression: restore returns index to original hash",
        restore.RestoredIndex && HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        Check("compression: pet bytes back after restore",
            LiveBytes(read, petPath).SequenceEqual(petBytes));
        Check("compression: art texture bytes back after restore",
            LiveBytes(read, artTex).SequenceEqual(new byte[] { 5, 6, 7, 8 }));
    }
}

// ---------------------------------------------------------------------------
// WriteStrategy vocabulary + settings/profile migration
// ---------------------------------------------------------------------------
{
    Check("write-strategy: keys roundtrip",
        WriteStrategies.ToKey(WriteStrategy.Template) == "template" &&
        WriteStrategies.ToKey(WriteStrategy.Empty) == "empty" &&
        WriteStrategies.ToKey(WriteStrategy.CompressionOnly) == "compress-only" &&
        WriteStrategies.ToKey(WriteStrategy.CompressThenTemplate) == "compress-then-template" &&
        WriteStrategies.ToKey(WriteStrategy.CompressThenEmpty) == "compress-then-empty");
    Check("write-strategy: parse accepts keys and legacy aliases",
        WriteStrategies.TryParse("compress-only", out var co) && co == WriteStrategy.CompressionOnly &&
        WriteStrategies.TryParse("empty", out var e) && e == WriteStrategy.Empty &&
        WriteStrategies.TryParse("valid", out var v) && v == WriteStrategy.Template &&
        !WriteStrategies.TryParse("bogus", out _));
    Check("write-strategy: unknown/absent means template",
        WriteStrategies.FromKey(null) == WriteStrategy.Template &&
        WriteStrategies.FromKey("bogus") == WriteStrategy.Template);
    Check("write-strategy: derived blank style",
        WriteStrategies.ToBlankStyle(WriteStrategy.Empty) == BlankStyle.EmptyBytes &&
        WriteStrategies.ToBlankStyle(WriteStrategy.CompressThenEmpty) == BlankStyle.EmptyBytes &&
        WriteStrategies.ToBlankStyle(WriteStrategy.Template) == BlankStyle.Template &&
        WriteStrategies.ToBlankStyle(WriteStrategy.CompressThenTemplate) == BlankStyle.Template &&
        WriteStrategies.ToBlankStyle(WriteStrategy.CompressionOnly) == BlankStyle.Template);
    Check("write-strategy: compression-containing and fallback flags",
        WriteStrategies.IsCompressionContaining(WriteStrategy.CompressionOnly) &&
        WriteStrategies.IsCompressionContaining(WriteStrategy.CompressThenTemplate) &&
        WriteStrategies.IsCompressionContaining(WriteStrategy.CompressThenEmpty) &&
        !WriteStrategies.IsCompressionContaining(WriteStrategy.Template) &&
        !WriteStrategies.IsCompressionContaining(WriteStrategy.Empty) &&
        WriteStrategies.FallsBackToBlank(WriteStrategy.CompressThenTemplate) &&
        WriteStrategies.FallsBackToBlank(WriteStrategy.CompressThenEmpty) &&
        !WriteStrategies.FallsBackToBlank(WriteStrategy.CompressionOnly));

    // Migration: an old settings file with only BlankStyle keeps its meaning; the new key wins.
    Check("write-strategy: old blank-style empty migrates to Empty",
        new AppSettings { BlankStyle = "empty" }.EffectiveWriteStrategy() == WriteStrategy.Empty);
    Check("write-strategy: old blank-style template migrates to Template",
        new AppSettings { BlankStyle = "template" }.EffectiveWriteStrategy() == WriteStrategy.Template);
    Check("write-strategy: new key wins over the legacy mirror",
        new AppSettings { BlankStyle = "empty", WriteStrategy = "compress-then-template" }
            .EffectiveWriteStrategy() == WriteStrategy.CompressThenTemplate);
    var setSettings = new AppSettings();
    setSettings.SetWriteStrategy(WriteStrategy.CompressThenEmpty);
    Check("write-strategy: SetWriteStrategy refreshes the legacy mirror",
        setSettings.WriteStrategy == "compress-then-empty" && setSettings.BlankStyle == "empty" &&
        setSettings.EffectiveWriteStrategy() == WriteStrategy.CompressThenEmpty);
    var settingsRt = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
        System.Text.Json.JsonSerializer.Serialize(setSettings))!;
    Check("write-strategy: settings roundtrip preserves strategy",
        settingsRt.EffectiveWriteStrategy() == WriteStrategy.CompressThenEmpty);

    Check("write-strategy: old profile blank-style migrates",
        new Profile { Name = "old", BlankStyle = "empty" }.EffectiveWriteStrategy() == WriteStrategy.Empty);
    var setProfile = new Profile { Name = "p" };
    setProfile.SetWriteStrategy(WriteStrategy.CompressionOnly);
    var profileRt = System.Text.Json.JsonSerializer.Deserialize<Profile>(
        System.Text.Json.JsonSerializer.Serialize(setProfile))!;
    Check("write-strategy: profile roundtrip preserves strategy and mirror",
        profileRt.EffectiveWriteStrategy() == WriteStrategy.CompressionOnly &&
        profileRt.BlankStyle == "template");
}

// ---------------------------------------------------------------------------
// Mesh LOD swap: mLOD parser, sibling probing, splice (M1)
// ---------------------------------------------------------------------------
{
    var gridA = TestFmtBuilder.Grid(5);
    var parsed = FmtMesh.TryParse(gridA);
    Check("fmt: layout A grid parses",
        parsed != null && parsed.Tris == 32 && parsed.Verts == 25 && parsed.Stride == 24 &&
        !parsed.LayoutB && parsed.VertexStart == 252 && parsed.VertexEnd == 852,
        parsed == null ? "null" : $"tris={parsed.Tris} verts={parsed.Verts} stride={parsed.Stride}");
    var gridB = TestFmtBuilder.Grid(5, layoutB: true);
    var parsedB = FmtMesh.TryParse(gridB);
    Check("fmt: layout B grid parses and is flagged",
        parsedB != null && parsedB.LayoutB && parsedB.VertexStart == 260 && parsedB.Tris == 32);

    Check("fmt: non-mLOD refused", FmtMesh.TryParse(new byte[200]) == null);
    Check("fmt: truncated refused", FmtMesh.TryParse(gridA[..40]) == null);
    var badCount = (byte[])gridA.Clone();
    BitConverter.GetBytes(99u).CopyTo(badCount, 56);
    Check("fmt: inconsistent index count refused", FmtMesh.TryParse(badCount) == null);
    var badIndex = (byte[])gridA.Clone();
    BitConverter.GetBytes((ushort)999).CopyTo(badIndex, 60);
    Check("fmt: out-of-range index refused", FmtMesh.TryParse(badIndex) == null);
    var degenerate = (byte[])gridA.Clone();
    BitConverter.GetBytes(BitConverter.ToUInt16(gridA, 60)).CopyTo(degenerate, 62);
    Check("fmt: degenerate triangle refused", FmtMesh.TryParse(degenerate) == null);
    var nanBox = (byte[])gridA.Clone();
    BitConverter.GetBytes(float.NaN).CopyTo(nanBox, 7);
    Check("fmt: NaN bbox refused", FmtMesh.TryParse(nanBox) == null);

    var ringCandidates = FmtMesh.LodSiblingCandidates("art/particles/fx/mp_ring_lod00_ff.fmt").ToList();
    Check("fmt: lod00 siblings cover padded, unpadded and trimmed suffixes",
        ringCandidates.Contains("art/particles/fx/mp_ring_lod01_ff.fmt") &&
        ringCandidates.Contains("art/particles/fx/mp_ring_lod1_ff.fmt") &&
        ringCandidates.Contains("art/particles/fx/mp_ring_lod01.fmt") &&
        ringCandidates.Contains("art/particles/fx/mp_ring_lod02_ff.fmt") &&
        !ringCandidates.Contains("art/particles/fx/mp_ring_lod00_ff.fmt"));
    var cylCandidates = FmtMesh.LodSiblingCandidates("art/particles/fx/mp_cyl_lod0_ff.fmt").ToList();
    Check("fmt: lod0 sibling variants",
        cylCandidates.Contains("art/particles/fx/mp_cyl_lod1.fmt") &&
        cylCandidates.Contains("art/particles/fx/mp_cyl_lod2_ff.fmt"));
    Check("fmt: non-lod and higher-lod sources yield nothing",
        !FmtMesh.LodSiblingCandidates("art/particles/fx/rock.fmt").Any() &&
        !FmtMesh.LodSiblingCandidates("art/particles/fx/mp_cyl_lod2_ff.fmt").Any());

    var big = TestFmtBuilder.Grid(9, name: "mp_ring_lod00_ff");   // 128 tris, 81 verts
    var small = TestFmtBuilder.Grid(5, name: "mp_ring_lod01_ff"); // 32 tris, 25 verts
    var swapped = FmtMesh.TryLodSwap(big, small);
    Check("fmt: lod swap produces a smaller valid mesh",
        swapped != null && swapped.Length < big.Length &&
        FmtMesh.TryParse(swapped) is { Tris: 32, Verts: 25, LayoutB: false });
    if (swapped != null)
    {
        var origGeo = FmtMesh.TryParse(big)!;
        var sibGeo = FmtMesh.TryParse(small)!;
        var trailer = big[origGeo.VertexEnd..];
        Check("fmt: swap keeps sibling geometry and original identity trailer",
            swapped[..sibGeo.VertexEnd].SequenceEqual(small[..sibGeo.VertexEnd]) &&
            swapped[sibGeo.VertexEnd..].SequenceEqual(trailer));
    }
    Check("fmt: swap refuses a sibling with more triangles", FmtMesh.TryLodSwap(small, big) == null);
    Check("fmt: swap refuses layout B on either side",
        FmtMesh.TryLodSwap(TestFmtBuilder.Grid(9, layoutB: true), small) == null &&
        FmtMesh.TryLodSwap(big, TestFmtBuilder.Grid(5, layoutB: true)) == null);
    Check("fmt: swap refuses garbage", FmtMesh.TryLodSwap(big, new byte[100]) == null);

    // Integration: plan -> apply -> verify -> restore with a real LOD pair in the fixture.
    var root = TestIndexBuilder.NewRoot("meshlod");
    var bundles = Path.Combine(root, "Bundles2");
    var ringPath = "art/particles/fx/mp_ring_lod00_ff.fmt";
    var sibPath = "art/particles/fx/mp_ring_lod01_ff.fmt";
    var lonelyPath = "art/particles/fx/lonely_lod0.fmt";
    var monsterPath = "art/models/effects/monster_particles/m_lod0.fmt";
    var monsterSib = "art/models/effects/monster_particles/m_lod1.fmt";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (ringPath, big),
        (sibPath, small),
        (lonelyPath, TestFmtBuilder.Grid(7)),
        (monsterPath, TestFmtBuilder.Grid(9)),
        (monsterSib, TestFmtBuilder.Grid(5))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (fmtPack, _) = service.BuildRules(game, "Mesh LOD Test", "test");
    RulePackStore.Save(fmtPack, Path.Combine(root, "pack.json"));

    ApplyRequest Req(bool lod, bool decimate, RiskTier tier) => new()
    {
        Game = game,
        Pack = fmtPack,
        RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = tier,
        WriteStrategy = WriteStrategy.CompressionOnly,
        Compression = new CompressionOptions { MeshLod = lod, MeshDecimate = decimate, DecimateStrength = 60 },
        Force = true
    };

    var plan = service.BuildPlan(Req(true, false, RiskTier.Fx));
    var ringChange = plan.Changes.FirstOrDefault(c => c.Path == ringPath);
    Check("meshlod: lod0 mesh planned as SwapMeshLod with precomputed bytes",
        ringChange != null && ringChange.Strategy == StrategyKind.SwapMeshLod &&
        ringChange.Tier == RiskTier.Fx && ringChange.Replacement != null &&
        ringChange.Replacement.Length < big.Length);
    Check("meshlod: sibling itself is not a candidate",
        !plan.Changes.Any(c => c.Path == sibPath));
    Check("meshlod: mesh without sibling is unsupported, not guessed",
        plan.Unsupported.Contains(lonelyPath));
    Check("meshlod: monster meshes stay gameplay-ignored",
        plan.Ignored.Contains(monsterPath) && !plan.Changes.Any(c => c.Path == monsterPath));
    Check("meshlod: safe tier skips the swap",
        service.BuildPlan(Req(true, false, RiskTier.Safe)).SkippedExperimental.Contains(ringPath));

    var originalHash = HashUtil.Sha256File(indexPath);
    var applied = service.Apply(Req(true, false, RiskTier.Fx));
    Check("meshlod: apply writes exactly the planned replacement",
        applied.Plan.Changes.Count(c => c.Strategy == StrategyKind.SwapMeshLod) == 1);
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(ringPath, out var ringRecord);
        var live = ringRecord!.Read().ToArray();
        Check("meshlod: live bytes equal the plan replacement",
            ringChange!.Replacement != null && live.SequenceEqual(ringChange.Replacement));
        Check("meshlod: live mesh parses with reduced triangles",
            FmtMesh.TryParse(live) is { Tris: 32, Verts: 25 });
        read.TryGetFile(sibPath, out var sibRecord);
        Check("meshlod: sibling file untouched", sibRecord!.Read().ToArray().SequenceEqual(small));
        read.TryGetFile(monsterPath, out var monRecord);
        Check("meshlod: monster mesh untouched", monRecord!.Read().ToArray().SequenceEqual(TestFmtBuilder.Grid(9)));
    }
    var status = service.GetStatus(game);
    Check("meshlod: status modified with custom bundles", status.IsModified && status.CustomBundlesExist);

    var again = service.Apply(Req(true, false, RiskTier.Fx));
    Check("meshlod: re-apply rolls back to baseline and reproduces the swap",
        again.Plan.Changes.Count(c => c.Strategy == StrategyKind.SwapMeshLod) == 1);

    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("meshlod: restore returns the original index hash",
        restore.RestoredIndex &&
        HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(ringPath, out var ringRecord);
        Check("meshlod: original mesh bytes back", ringRecord!.Read().ToArray().SequenceEqual(big));
    }
}

// ---------------------------------------------------------------------------
// Mesh decimation: clustering, strength curve, floors, full loop (M2)
// ---------------------------------------------------------------------------
{
    // Structural validation at the known stride: sparse output can be stride-ambiguous to the
    // heuristic re-parser, but the counts, indices, attributes and trailer must be exact.
    static bool ValidDecimation(byte[] orig, byte[] result, int stride)
    {
        var og = FmtMesh.TryParse(orig);
        if (og == null || result.Length < 60)
            return false;
        if (!result[..44].SequenceEqual(orig[..44]) || !result[52..56].SequenceEqual(orig[52..56]))
            return false;
        int nt = BitConverter.ToInt32(result, 44), nv = BitConverter.ToInt32(result, 48);
        int ni = BitConverter.ToInt32(result, 56);
        if (nt < 2 || nv < 4 || ni != 3 * nt || nt * 20 > og.Tris * 19)
            return false;
        for (var i = 0; i < ni; i++)
            if (BitConverter.ToUInt16(result, 60 + 2 * i) >= nv)
                return false;
        for (var i = 0; i < ni; i += 3)
        {
            var a = BitConverter.ToUInt16(result, 60 + 2 * i);
            var b = BitConverter.ToUInt16(result, 62 + 2 * i);
            var c = BitConverter.ToUInt16(result, 64 + 2 * i);
            if (a == b || b == c || a == c)
                return false;
        }
        var vs = 60 + 2 * ni;
        var trailerLen = orig.Length - og.VertexEnd;
        if (result.Length != vs + nv * stride + trailerLen)
            return false;
        if (!result[(vs + nv * stride)..].SequenceEqual(orig[og.VertexEnd..]))
            return false;
        for (var v = 0; v < nv; v++)
            for (var k = 12; k < stride; k++)
                if (result[vs + v * stride + k] != 0x80)
                    return false; // packed attributes carried verbatim
        return true;
    }

    var grid = TestFmtBuilder.Grid(9); // 128 tris, 81 verts, unique positions
    var d50 = FmtMesh.TryDecimate(grid, 50);
    Check("decimate: strength 50 shrinks a valid mesh",
        d50 != null && d50.Length < grid.Length && ValidDecimation(grid, d50, 24));
    Check("decimate: result re-parses at stride 24",
        d50 != null && FmtMesh.TryParse(d50) is { LayoutB: false, Stride: 24 });
    var d25 = FmtMesh.TryDecimate(grid, 25);
    var d100 = FmtMesh.TryDecimate(grid, 100);
    Check("decimate: stronger settings keep fewer triangles",
        d25 != null && d100 != null &&
        BitConverter.ToInt32(d100, 44) < BitConverter.ToInt32(d25, 44) &&
        ValidDecimation(grid, d25, 24) && ValidDecimation(grid, d100, 24));
    Check("decimate: zero/negative strength is a no-op",
        FmtMesh.TryDecimate(grid, 0) == null && FmtMesh.TryDecimate(grid, -10) == null);
    Check("decimate: billboard quad refused",
        FmtMesh.TryDecimate(TestFmtBuilder.Grid(2), 100) == null);
    Check("decimate: layout B refused",
        FmtMesh.TryDecimate(TestFmtBuilder.Grid(9, layoutB: true), 80) == null);
    Check("decimate: garbage refused", FmtMesh.TryDecimate(new byte[300], 80) == null);

    var grid28 = TestFmtBuilder.Grid(9, stride: 28);
    var d28 = FmtMesh.TryDecimate(grid28, 60);
    Check("decimate: stride-28 vertices carried at their own stride",
        d28 != null && ValidDecimation(grid28, d28, 28));

    // Collapse floor: three distinct positions can never form the 4-vertex minimum.
    var fewPos = TestFmtBuilder.Build(4, 6,
        [0, 1, 2, 1, 2, 3, 2, 3, 4, 3, 4, 5],
        [0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0]);
    Check("decimate: collapse below four vertices refused", FmtMesh.TryDecimate(fewPos, 100) == null);

    // Integration: decimation fallback, LOD precedence, apply/verify/restore.
    var root = TestIndexBuilder.NewRoot("decimate");
    var bundles = Path.Combine(root, "Bundles2");
    var blobPath = "art/particles/fx/blob.fmt";
    var ringPath = "art/particles/fx/mp_ring_lod00_ff.fmt";
    var sibPath = "art/particles/fx/mp_ring_lod01_ff.fmt";
    var monsterPath = "art/models/effects/monster_particles/m.fmt";
    var big = TestFmtBuilder.Grid(9);
    var small = TestFmtBuilder.Grid(5);
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (blobPath, big),
        (ringPath, big),
        (sibPath, small),
        (monsterPath, big)
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (decPack, _) = service.BuildRules(game, "Decimate Test", "test");
    RulePackStore.Save(decPack, Path.Combine(root, "pack.json"));

    ApplyRequest Req(bool lod, bool dec, RiskTier tier) => new()
    {
        Game = game,
        Pack = decPack,
        RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = tier,
        WriteStrategy = WriteStrategy.CompressionOnly,
        Compression = new CompressionOptions { MeshLod = lod, MeshDecimate = dec, DecimateStrength = 60 },
        Force = true
    };

    var plan = service.BuildPlan(Req(false, true, RiskTier.Fx));
    var blobChange = plan.Changes.FirstOrDefault(c => c.Path == blobPath);
    Check("decimate: sibling-less mesh planned as DecimateMesh",
        blobChange != null && blobChange.Strategy == StrategyKind.DecimateMesh &&
        blobChange.Tier == RiskTier.Fx && blobChange.Replacement != null &&
        ValidDecimation(big, blobChange.Replacement, 24));
    Check("decimate: monster mesh still ignored",
        plan.Ignored.Contains(monsterPath) && !plan.Changes.Any(c => c.Path == monsterPath));

    var both = service.BuildPlan(Req(true, true, RiskTier.Fx));
    Check("decimate: official LOD wins over decimation when a sibling exists",
        both.Changes.First(c => c.Path == ringPath).Strategy == StrategyKind.SwapMeshLod &&
        both.Changes.First(c => c.Path == blobPath).Strategy == StrategyKind.DecimateMesh);
    Check("decimate: safe tier skips decimation",
        service.BuildPlan(Req(false, true, RiskTier.Safe)).SkippedExperimental.Contains(blobPath));

    var originalHash = HashUtil.Sha256File(indexPath);
    var applied = service.Apply(Req(false, true, RiskTier.Fx));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(blobPath, out var blobRecord);
        Check("decimate: live bytes equal the planned replacement",
            blobChange!.Replacement != null &&
            blobRecord!.Read().ToArray().SequenceEqual(blobChange.Replacement));
    }
    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("decimate: restore returns original bytes and hash",
        restore.RestoredIndex &&
        HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(blobPath, out var blobRecord);
        Check("decimate: original mesh back after restore",
            blobRecord!.Read().ToArray().SequenceEqual(big));
    }
}

// ---------------------------------------------------------------------------
// Shader simplification: pass-through contract, whitelist, full loop (M3)
// ---------------------------------------------------------------------------
{
    static byte[] U16Graph(string json)
    {
        var body = Encoding.Unicode.GetBytes(json);
        var b = new byte[2 + body.Length];
        b[0] = 0xFF; b[1] = 0xFE;
        body.CopyTo(b, 2);
        return b;
    }

    static string ComplexGraphJson()
    {
        var nodes = new List<string>
        {
            "{\"type\":\"InputAlbedoColor\",\"index\":0,\"stage\":\"Texturing\"}",
            "{\"type\":\"InputEmissiveColor\",\"index\":0,\"stage\":\"Texturing\"}",
            "{\"type\":\"AlbedoColor\",\"index\":0,\"stage\":\"Texturing\"}",
            "{\"type\":\"EmissiveColor\",\"index\":0,\"stage\":\"Texturing\"}",
            "{\"type\":\"ConstantUInt\",\"index\":0,\"custom_parameter\":\"Mode\"}",
            "{\"type\":\"ConstantUInt\",\"index\":1,\"custom_parameter\":\"Strength\"}"
        };
        for (var i = 0; i < 7; i++) nodes.Add($"{{\"type\":\"Multiply\",\"index\":{i}}}");
        for (var i = 0; i < 4; i++) nodes.Add($"{{\"type\":\"Lerp\",\"index\":{i}}}");
        for (var i = 0; i < 6; i++) nodes.Add($"{{\"type\":\"SelectFloat3\",\"index\":{i}}}");
        var links = new List<string>
        {
            "{\"src\":{\"type\":\"InputAlbedoColor\",\"index\":0,\"stage\":\"Texturing\",\"variable\":\"output\"},\"dst\":{\"type\":\"Multiply\",\"index\":0,\"variable\":\"input\"}}",
            "{\"src\":{\"type\":\"Multiply\",\"index\":0,\"variable\":\"output\"},\"dst\":{\"type\":\"Lerp\",\"index\":0,\"variable\":\"input\"}}",
            "{\"src\":{\"type\":\"Lerp\",\"index\":0,\"variable\":\"output\"},\"dst\":{\"type\":\"AlbedoColor\",\"index\":0,\"stage\":\"Texturing\",\"variable\":\"input\"}}",
            "{\"src\":{\"type\":\"InputEmissiveColor\",\"index\":0,\"stage\":\"Texturing\",\"variable\":\"output\"},\"dst\":{\"type\":\"EmissiveColor\",\"index\":0,\"stage\":\"Texturing\",\"variable\":\"input\"}}"
        };
        return "{\"version\":6,\"shader_group\":[\"Material\",\"Temporary\"],\"nodes\":[" +
               string.Join(",", nodes) + "],\"links\":[" + string.Join(",", links) +
               "],\"unused_links\":[{\"src\":{\"type\":\"Multiply\",\"index\":3,\"variable\":\"output\"}}]}";
    }

    var graphPath = "metadata/effects/graphs/general/masks/mask_fresnelstatic.fxgraph";
    var complex = U16Graph(ComplexGraphJson());
    var simplified = FxGraphSimplify.TrySimplify(complex, graphPath);
    Check("fxgraph: whitelisted complex graph simplifies",
        simplified != null && simplified.Length < complex.Length &&
        simplified[0] == 0xFF && simplified[1] == 0xFE);
    if (simplified != null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            Encoding.Unicode.GetString(simplified, 2, simplified.Length - 2));
        var root = doc.RootElement;
        Check("fxgraph: version and shader_group preserved",
            root.GetProperty("version").GetInt32() == 6 &&
            root.GetProperty("shader_group")[0].GetString() == "Material");
        Check("fxgraph: unused_links dropped", !root.TryGetProperty("unused_links", out _));
        var types = root.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()!).ToList();
        Check("fxgraph: inputs, registers and parameter declarations kept",
            types.Contains("InputAlbedoColor") && types.Contains("AlbedoColor") &&
            types.Contains("InputEmissiveColor") && types.Contains("EmissiveColor") &&
            types.Count(t => t == "ConstantUInt") == 2);
        Check("fxgraph: shading math removed",
            !types.Contains("Multiply") && !types.Contains("Lerp") && !types.Contains("SelectFloat3"));
        var links = root.GetProperty("links").EnumerateArray().ToList();
        Check("fxgraph: pass-through rewiring",
            links.Count == 2 &&
            links.Any(l => l.GetProperty("src").GetProperty("type").GetString() == "InputAlbedoColor" &&
                           l.GetProperty("dst").GetProperty("type").GetString() == "AlbedoColor" &&
                           l.GetProperty("dst").GetProperty("variable").GetString() == "input") &&
            links.Any(l => l.GetProperty("src").GetProperty("type").GetString() == "InputEmissiveColor"));
        var keptKeys = root.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString() + ":" +
                         (n.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0)).ToHashSet();
        Check("fxgraph: no dangling link endpoints",
            links.All(l =>
                keptKeys.Contains(l.GetProperty("src").GetProperty("type").GetString() + ":" +
                                  (l.GetProperty("src").TryGetProperty("index", out var si) ? si.GetInt32() : 0)) &&
                keptKeys.Contains(l.GetProperty("dst").GetProperty("type").GetString() + ":" +
                                  (l.GetProperty("dst").TryGetProperty("index", out var di) ? di.GetInt32() : 0))));
    }

    Check("fxgraph: non-whitelisted names refused",
        FxGraphSimplify.TrySimplify(complex, "metadata/effects/graphs/general/masks/mask_animated_particle.fxgraph") == null &&
        FxGraphSimplify.TrySimplify(complex, "metadata/effects/graphs/general/rotate_local.fxgraph") == null &&
        FxGraphSimplify.TrySimplify(complex, "metadata/effects/spells/fire/custom.fxgraph") == null);
    Check("fxgraph: version 3 refused",
        FxGraphSimplify.TrySimplify(U16Graph("{\"version\":3,\"lighting_disabled\":true}"), graphPath) == null);
    Check("fxgraph: garbage refused",
        FxGraphSimplify.TrySimplify(U16Graph("not json"), graphPath) == null &&
        FxGraphSimplify.TrySimplify(new byte[] { 1, 2, 3 }, graphPath) == null);
    Check("fxgraph: utf-16 big-endian refused",
        FxGraphSimplify.TrySimplify(new byte[] { 0xFE, 0xFF, 0, 123 }, graphPath) == null);
    var utf8 = Encoding.UTF8.GetBytes(ComplexGraphJson());
    var utf8Out = FxGraphSimplify.TrySimplify(utf8, graphPath);
    Check("fxgraph: utf-8 in, utf-8 out",
        utf8Out != null && utf8Out[0] != 0xFF &&
        System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(utf8Out)).RootElement
            .GetProperty("version").GetInt32() == 6);
    var minimal = U16Graph("{\"version\":6,\"nodes\":[" +
        "{\"type\":\"InputAlbedoColor\",\"index\":0},{\"type\":\"AlbedoColor\",\"index\":0}]," +
        "\"links\":[]}");
    Check("fxgraph: already-minimal graph refused (no meaningful reduction)",
        FxGraphSimplify.TrySimplify(minimal, graphPath) == null);

    // Integration: shader switch simplifies the whitelisted graph, monster copies stay ignored,
    // and the whole apply/verify/restore loop carries the precomputed bytes.
    var root2 = TestIndexBuilder.NewRoot("fxgraph");
    var bundles = Path.Combine(root2, "Bundles2");
    var monsterGraph = "metadata/effects/spells/monsters_effects/boss/graph/mask_fresnelstatic.fxgraph";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (graphPath, complex),
        (monsterGraph, complex),
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );"))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root2, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (fxPack, _) = service.BuildRules(game, "FxGraph Test", "test");
    RulePackStore.Save(fxPack, Path.Combine(root2, "pack.json"));

    var request = new ApplyRequest
    {
        Game = game,
        Pack = fxPack,
        RulePackPath = Path.Combine(root2, "pack.json"),
        Preset = "safe",
        Ignore = IgnoreList.Default(),
        MaxTier = RiskTier.Fx,
        WriteStrategy = WriteStrategy.CompressionOnly,
        Compression = new CompressionOptions { Shader = true },
        Force = true
    };
    var plan = service.BuildPlan(request);
    var graphChange = plan.Changes.FirstOrDefault(c => c.Path == graphPath);
    Check("fxgraph: planned as SimplifyGraph instead of blanking",
        graphChange != null && graphChange.Strategy == StrategyKind.SimplifyGraph &&
        graphChange.Tier == RiskTier.Fx && graphChange.Replacement != null);
    Check("fxgraph: monster copy gameplay-ignored",
        plan.Ignored.Contains(monsterGraph) && !plan.Changes.Any(c => c.Path == monsterGraph));
    Check("fxgraph: safe tier skips simplification",
        service.BuildPlan(new ApplyRequest
        {
            Game = game, Pack = fxPack, RulePackPath = Path.Combine(root2, "pack.json"),
            Preset = "safe", Ignore = IgnoreList.Default(), MaxTier = RiskTier.Safe,
            WriteStrategy = WriteStrategy.CompressionOnly,
            Compression = new CompressionOptions { Shader = true }, Force = true
        }).SkippedExperimental.Contains(graphPath));

    var originalHash = HashUtil.Sha256File(indexPath);
    service.Apply(request);
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(graphPath, out var graphRecord);
        Check("fxgraph: live bytes equal the planned replacement",
            graphChange!.Replacement != null &&
            graphRecord!.Read().ToArray().SequenceEqual(graphChange.Replacement));
        read.TryGetFile(monsterGraph, out var monRecord);
        Check("fxgraph: monster graph untouched", monRecord!.Read().ToArray().SequenceEqual(complex));
    }
    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("fxgraph: restore returns original bytes and hash",
        restore.RestoredIndex &&
        HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        read.TryGetFile(graphPath, out var graphRecord);
        Check("fxgraph: original graph back after restore",
            graphRecord!.Read().ToArray().SequenceEqual(complex));
    }
}

// ---------------------------------------------------------------------------
// Texture compression: BC7 decode, BC1/BC3 encode, DdsCodec policy (M4)
// ---------------------------------------------------------------------------
{
    static uint Dxgi(byte[] d) => BitConverter.ToUInt32(d, 128);
    static int TexW(byte[] d) => (int)BitConverter.ToUInt32(d, 16);
    static int TexH(byte[] d) => (int)BitConverter.ToUInt32(d, 12);
    static int TexMips(byte[] d) => (int)BitConverter.ToUInt32(d, 28);
    static double Psnr(byte[] a, byte[] b)
    {
        double se = 0;
        var n = Math.Min(a.Length, b.Length) / 4 * 3;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i += 4)
            for (var k = 0; k < 3; k++)
            {
                var d = a[i + k] - b[i + k];
                se += d * d;
            }
        var mse = se / n;
        return mse == 0 ? 99.0 : 10 * Math.Log10(255 * 255 / mse);
    }

    // BC7 decoder known-answer: every mode reproduces a constant color exactly.
    var whiteOk = true;
    var blackOk = true;
    Span<byte> px = stackalloc byte[64];
    for (var mn = 1; mn <= 8; mn++)
    {
        Bc7Decoder.DecodeBlock(TestBc7Builder.ConstBlock(mn, 255, 255, 255, 255), px);
        for (var t = 0; t < 16; t++)
            if (px[t * 4] != 255 || px[t * 4 + 1] != 255 || px[t * 4 + 2] != 255 || px[t * 4 + 3] != 255)
                whiteOk = false;
        var expA = mn >= 5 ? 0 : 255; // alpha modes carry 0; RGB-only modes force opaque
        Bc7Decoder.DecodeBlock(TestBc7Builder.ConstBlock(mn, 0, 0, 0, 0), px);
        for (var t = 0; t < 16; t++)
            if (px[t * 4] != 0 || px[t * 4 + 1] != 0 || px[t * 4 + 2] != 0 || px[t * 4 + 3] != expA)
                blackOk = false;
    }
    Check("bc7: all 8 modes decode constant white exactly", whiteOk);
    Check("bc7: all 8 modes decode constant black exactly", blackOk);

    Bc7Decoder.DecodeBlock(TestBc7Builder.SmoothAlphaBlock(), px);
    var smoothSeen = false;
    for (var t = 0; t < 16; t++)
        if (px[t * 4 + 3] is > 0 and < 255) smoothSeen = true;
    Check("bc7: mode-6 dual-index decodes smooth alpha", smoothSeen);

    Span<byte> reserved = stackalloc byte[16];
    Bc7Decoder.DecodeBlock(reserved, px);
    var reservedBlack = true;
    for (var t = 0; t < 64; t++) if (px[t] != 0) reservedBlack = false;
    Check("bc7: reserved mode 0 decodes transparent black", reservedBlack);

    // BC1 round-trip: opaque gradient keeps color, cutout keeps its hard 0/255 silhouette.
    const int n = 32;
    var gradient = new byte[n * n * 4];
    for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var o = (y * n + x) * 4;
            gradient[o] = (byte)(x * 8); gradient[o + 1] = (byte)(y * 8);
            gradient[o + 2] = 128; gradient[o + 3] = 255;
        }
    var bc1 = Bc1Encoder.EncodeImage(gradient, n, n);
    var bc1Back = Bc1Encoder.DecodeImage(bc1, n, n);
    var opaqueAlpha = true;
    for (var i = 3; i < bc1Back.Length; i += 4) if (bc1Back[i] != 255) opaqueAlpha = false;
    Check("bc1: opaque gradient round-trips with high fidelity", Psnr(gradient, bc1Back) > 30 && opaqueAlpha,
        $"psnr={Psnr(gradient, bc1Back):F1}");
    Check("bc1: opaque image is half the BC7 byte size", bc1.Length == ((n + 3) / 4) * ((n + 3) / 4) * 8);

    var cutout = new byte[n * n * 4];
    for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var o = (y * n + x) * 4;
            cutout[o] = (byte)(x * 8); cutout[o + 1] = (byte)(y * 8); cutout[o + 2] = 200;
            cutout[o + 3] = (byte)(x < n / 2 ? 255 : 0);
        }
    var cutBack = Bc1Encoder.DecodeImage(Bc1Encoder.EncodeImage(cutout, n, n), n, n);
    var silhouette = true;
    for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var want = x < n / 2 ? 255 : 0;
            if (cutBack[(y * n + x) * 4 + 3] != want) silhouette = false;
        }
    Check("bc1: cutout silhouette (0/255 alpha) preserved exactly", silhouette);

    // BC3 round-trip: smooth alpha gradient survives (the soft-particle path).
    var soft = new byte[n * n * 4];
    for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var o = (y * n + x) * 4;
            soft[o] = 200; soft[o + 1] = 150; soft[o + 2] = 100;
            soft[o + 3] = (byte)(x * 8);
        }
    var bc3 = Bc3Encoder.EncodeImage(soft, n, n);
    var bc3Back = Bc3Encoder.DecodeImage(bc3, n, n);
    double ase = 0;
    for (var i = 3; i < soft.Length; i += 4) { var d = soft[i] - bc3Back[i]; ase += d * d; }
    var alphaPsnr = 10 * Math.Log10(255 * 255 / (ase / (n * n)));
    Check("bc3: smooth alpha round-trips with high fidelity", alphaPsnr > 30 && Psnr(soft, bc3Back) > 30,
        $"alphaPsnr={alphaPsnr:F1}");
    Check("bc3: block size is 16 bytes (same as BC7)", bc3.Length == ((n + 3) / 4) * ((n + 3) / 4) * 16);

    // DdsCodec policy on synthesized BC7 fixtures.
    var opaqueTex = TestBc7Builder.SolidDds(64, 64, 255, 255, 255, 255, mips: 3);
    var sameRes = DdsCodec.TryCompress(opaqueTex, false);
    Check("dds: opaque BC7 -> same-res BC1, dims & mips preserved, smaller",
        sameRes != null && Dxgi(sameRes) == 71 && TexW(sameRes) == 64 && TexH(sameRes) == 64 &&
        TexMips(sameRes) == 3 && sameRes.Length < opaqueTex.Length);

    var softTex = TestBc7Builder.SmoothDds(64, 64, mips: 3);
    Check("dds: smooth alpha refused for same-res BC1 (no honest win)",
        DdsCodec.TryCompress(softTex, false) == null);

    var softDown = DdsCodec.TryCompress(softTex, true);
    Check("dds: smooth alpha downscale -> BC3 at half resolution, smaller",
        softDown != null && Dxgi(softDown) == 77 && TexW(softDown) == 32 && TexH(softDown) == 32 &&
        softDown.Length < softTex.Length);

    var opaqueDown = DdsCodec.TryCompress(opaqueTex, true);
    Check("dds: opaque downscale -> BC1 at half resolution",
        opaqueDown != null && Dxgi(opaqueDown) == 71 && TexW(opaqueDown) == 32 && TexH(opaqueDown) == 32);

    // Non-power-of-two sizes must terminate the mip chain at 1x1 (regression: floor-halving looped).
    var oddOpaque = DdsCodec.TryCompress(TestBc7Builder.SolidDds(6, 6, 255, 255, 255, 255), true);
    var oddSoft = DdsCodec.TryCompress(TestBc7Builder.SmoothDds(6, 6), true);
    Check("dds: non-power-of-two downscale terminates at correct dims",
        oddOpaque != null && TexW(oddOpaque) == 3 && TexH(oddOpaque) == 3 &&
        oddSoft != null && TexW(oddSoft) == 3 && TexH(oddSoft) == 3 && Dxgi(oddSoft) == 77);

    // Refusals: anything that is not single-array 2D BC7 is left untouched.
    var legacy = (byte[])opaqueTex.Clone();
    legacy[84] = (byte)'D'; legacy[85] = (byte)'X'; legacy[86] = (byte)'T'; legacy[87] = (byte)'1';
    Check("dds: legacy DXT1 fourcc refused", DdsCodec.TryCompress(legacy, false) == null);
    Check("dds: already-BC1 (dxgi 71) refused",
        DdsCodec.TryCompress(TestBc7Builder.SolidDds(64, 64, 255, 255, 255, 255, dxgi: 71), false) == null);
    Check("dds: cubemap refused",
        DdsCodec.TryCompress(TestBc7Builder.SolidDds(64, 64, 255, 255, 255, 255, miscFlag: 4), false) == null);
    Check("dds: texture array refused",
        DdsCodec.TryCompress(TestBc7Builder.SolidDds(64, 64, 255, 255, 255, 255, arraySize: 2), false) == null);
    Check("dds: truncated header refused", DdsCodec.TryCompress(opaqueTex[..100], false) == null);
    Check("dds: 1x1 cannot downscale", DdsCodec.TryCompress(TestBc7Builder.SolidDds(1, 1, 255, 255, 255, 255), true) == null);
}

// ---------------------------------------------------------------------------
// Texture compression integration: apply -> verify bytes -> restore (M4)
// ---------------------------------------------------------------------------
{
    var root = TestIndexBuilder.NewRoot("texcompress");
    var bundles = Path.Combine(root, "Bundles2");
    var opaqueTex = TestBc7Builder.SolidDds(64, 64, 255, 255, 255, 255, mips: 3);
    var softTex = TestBc7Builder.SmoothDds(64, 64, mips: 3);
    var petBytes = new byte[] { 0x10, 0x20, 0x30 };
    const string opaquePath = "art/particles/fire/opaque.dds";
    const string softPath = "art/particles/fire/soft.dds";
    const string monsterPath = "art/particles/monster_particles/m.dds";
    const string petPath = "metadata/effects/spells/fire/burn.pet";
    var indexPath = TestIndexBuilder.Build(bundles, new[]
    {
        (opaquePath, opaqueTex),
        (softPath, softTex),
        ("art/particles/fire/spark_n.dds", opaqueTex),
        (monsterPath, opaqueTex),
        (petPath, petBytes),
        ("metadata/effects/spells/fire/fireball.ao", Encoding.UTF8.GetBytes("AddAttached( 'x.pet', <root> );"))
    });
    var game = SteamLibraryLocator.FromIndex(indexPath);
    var appPaths = new AppPaths(Path.Combine(root, "home"));
    appPaths.Ensure();
    var service = new AppService(appPaths);
    var (texPack, _) = service.BuildRules(game, "TexCompress Test", "test");
    RulePackStore.Save(texPack, Path.Combine(root, "pack.json"));

    ApplyRequest Req(CompressionOptions c) => new()
    {
        Game = game, Pack = texPack, RulePackPath = Path.Combine(root, "pack.json"),
        Preset = "safe", Ignore = IgnoreList.Default(), MaxTier = RiskTier.Material,
        WriteStrategy = WriteStrategy.CompressionOnly,
        Compression = c, Force = true
    };
    static byte[] TexLive(IndexSession s, string p)
    {
        s.TryGetFile(p, out var r);
        return r!.Read().ToArray();
    }

    var plan = service.BuildPlan(Req(new CompressionOptions { Texture = true }));
    var opaqueChange = plan.Changes.FirstOrDefault(c => c.Path == opaquePath);
    Check("tex integration: opaque planned as CompressDds (material tier, precomputed)",
        opaqueChange != null && opaqueChange.Strategy == StrategyKind.CompressDds &&
        opaqueChange.Tier == RiskTier.Material && opaqueChange.Replacement != null);
    Check("tex integration: smooth same-res unsupported, normal map not a candidate, monster ignored",
        plan.Unsupported.Contains(softPath) &&
        !plan.Changes.Any(c => c.Path == "art/particles/fire/spark_n.dds") &&
        plan.Ignored.Contains(monsterPath));

    var originalHash = HashUtil.Sha256File(indexPath);
    service.Apply(Req(new CompressionOptions { Texture = true }));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        Check("tex integration: live opaque bytes equal planned BC1 replacement",
            opaqueChange!.Replacement != null &&
            TexLive(read, opaquePath).SequenceEqual(opaqueChange.Replacement) &&
            BitConverter.ToUInt32(TexLive(read, opaquePath), 128) == 71);
        Check("tex integration: smooth texture untouched on disk",
            TexLive(read, softPath).SequenceEqual(softTex));
        Check("tex integration: no compression change targets .pet",
            !plan.Changes.Any(c => c.Path == petPath && CompressionOptions.IsCompressionStrategy(c.Strategy)));
    }
    var restore = service.Restore(new RestoreRequest { Game = game });
    Check("tex integration: restore returns original index hash",
        restore.RestoredIndex && HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        Check("tex integration: original opaque BC7 back after restore",
            TexLive(read, opaquePath).SequenceEqual(opaqueTex));
        Check("tex integration: pet bytes back after restore",
            TexLive(read, petPath).SequenceEqual(petBytes));
    }

    // Downscale tier: smooth now compresses to BC3, opaque to BC1, both at half resolution.
    var downPlan = service.BuildPlan(Req(new CompressionOptions { Texture = true, TextureDownscale = true }));
    var softChange = downPlan.Changes.FirstOrDefault(c => c.Path == softPath);
    Check("tex integration: downscale plans smooth as BC3 CompressDds",
        softChange != null && softChange.Strategy == StrategyKind.CompressDds && softChange.Replacement != null);
    service.Apply(Req(new CompressionOptions { Texture = true, TextureDownscale = true }));
    using (var read = IndexSession.OpenReadOnly(indexPath, bundles))
    {
        var liveSoft = TexLive(read, softPath);
        var liveOpaque = TexLive(read, opaquePath);
        Check("tex integration: downscale live soft is BC3 half-res, opaque is BC1 half-res",
            BitConverter.ToUInt32(liveSoft, 128) == 77 && BitConverter.ToUInt32(liveSoft, 16) == 32 &&
            BitConverter.ToUInt32(liveOpaque, 128) == 71 && BitConverter.ToUInt32(liveOpaque, 16) == 32);
    }
    var restore2 = service.Restore(new RestoreRequest { Game = game });
    Check("tex integration: restore after downscale returns original hash",
        restore2.RestoredIndex && HashUtil.Sha256File(indexPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase));
}


Console.WriteLine();
Console.WriteLine($"{tests - failures}/{tests} tests passed.");
return failures == 0 ? 0 : 1;