using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using PsApp;
using PsRules;

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .SetupWithoutStarting();

var home = Path.Combine(Path.GetTempPath(), "ps-ui-test", Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("POE2SMOOTHER_HOME", home);

var packPath = Path.Combine(home, "rulepacks", "default.json");
RulePackStore.Save(new RulePack
{
    Name = "UI Test",
    Categories =
    [
        new RuleCategory
        {
            Id = "skill.fire",
            Group = "skills",
            Label = new() { ["zh"] = "火焰", ["en"] = "Fire" },
            Rules =
            [
                new Rule
                {
                    Path = "metadata/effects/spells/fire/fireball.ao",
                    Strategy = StrategyKind.StripAttachments
                }
            ]
        },
        new RuleCategory
        {
            Id = "league.delirium",
            Group = "leagues",
            Label = new() { ["zh"] = "迷雾", ["en"] = "Delirium" },
            Rules =
            [
                new Rule
                {
                    Path = "metadata/effects/environment/league_delirium/fog.ao",
                    Strategy = StrategyKind.StripAttachments
                }
            ]
        },
        new RuleCategory
        {
            Id = "mtx.spells",
            Group = "mtx",
            Label = new() { ["zh"] = "外观", ["en"] = "MTX" },
            Rules =
            [
                new Rule
                {
                    Path = "metadata/effects/microtransactions/spells/mtx.ao",
                    Strategy = StrategyKind.StripAttachments
                }
            ]
        },
        new RuleCategory
        {
            Id = "mtx.rings",
            Group = "mtx",
            Label = new() { ["zh"] = "戒指", ["en"] = "Rings" },
            Rules =
            [
                new Rule
                {
                    Path = "metadata/effects/microtransactions/rings/ring.ao",
                    Strategy = StrategyKind.StripAttachments
                }
            ]
        }
    ],
    Presets = new()
    {
        ["safe"] = ["skill.*", "mtx.*"],
        ["clean"] = ["skill.*", "mtx.*", "league.*"],
        ["performance"] = ["league.*"],
        ["full"] = ["*"]
    }
}, packPath);

// The drop-downs hold items (bilingual labels), not bare tokens: select by id the way the app does.
static object ItemById(object source, string id) =>
    ((System.Collections.IEnumerable)source).Cast<object>()
        .First(i => (i.GetType().Name == "PresetItem" ? ((dynamic)i).Id
                     : i.GetType().Name == "LanguageItem" ? ((dynamic)i).Id
                     : (string)i) == id);

var window = new MainWindow();
var gamePath = window.FindControl<TextBox>("GamePathBox");
var categoryGroupList = window.FindControl<ItemsControl>("CategoryGroupList");
var searchBox = window.FindControl<TextBox>("CategorySearchBox");
var groupFilter = window.FindControl<ComboBox>("GroupFilterBox");
var emptyText = window.FindControl<TextBlock>("FilterEmptyText");
var ignoreBox = window.FindControl<TextBox>("IgnoreBox");

if (window.Title != "PoE2 Smoother") throw new Exception("unexpected title");
if (gamePath == null) throw new Exception("GamePathBox missing");
if (categoryGroupList == null) throw new Exception("CategoryGroupList missing");
if (searchBox == null) throw new Exception("CategorySearchBox missing");
if (groupFilter == null) throw new Exception("GroupFilterBox missing");
if (emptyText == null) throw new Exception("FilterEmptyText missing");
if (ignoreBox == null) throw new Exception("IgnoreBox missing");

var loadPack = typeof(MainWindow).GetMethod("LoadPack",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new Exception("LoadPack not found");
loadPack.Invoke(window, [packPath]);
Dispatcher.UIThread.RunJobs();

var groups = (ObservableCollection<CategoryGroupRow>)categoryGroupList.ItemsSource!;
if (groups.Count != 3) throw new Exception($"expected 3 groups, got {groups.Count}");

var skillsGroup = groups.Single(g => g.Group == "skills");
var leaguesGroup = groups.Single(g => g.Group == "leagues");
if (skillsGroup.IsSelected != true)
    throw new Exception("safe preset should select the skills group");
if (leaguesGroup.IsSelected != false)
    throw new Exception("safe preset should leave the leagues group unselected");

leaguesGroup.SetSelection(true);
if (leaguesGroup.IsSelected != true || leaguesGroup.Categories.Any(c => !c.IsSelected))
    throw new Exception("select whole group failed");

leaguesGroup.SetSelection(false);
if (leaguesGroup.IsSelected != false || leaguesGroup.Categories.Any(c => c.IsSelected))
    throw new Exception("clear whole group failed");

// ---------------------------------------------------------------------------
// Rows are bilingual; 语言 decides which side leads
// ---------------------------------------------------------------------------
var languageBox = window.FindControl<ComboBox>("LanguageBox") ?? throw new Exception("LanguageBox missing");
var fireZh = skillsGroup.Categories.Single(c => c.Id == "skill.fire");
if (fireZh.Label != "火焰" || fireZh.LabelSecondary != "Fire" || fireZh.CountText != "1 个文件")
    throw new Exception($"zh row should read 火焰 / Fire with a Chinese count: {fireZh.Label} | {fireZh.LabelSecondary} | {fireZh.CountText}");

// The shipped table overrides what the pack stored, which is how old rule packs get current
// wording without a GGPK re-scan.
var mtxZh = groups.Single(g => g.Group == "mtx").Categories.Single(c => c.Id == "mtx.spells");
if (mtxZh.Label != "法術外觀" || mtxZh.LabelSecondary != "MTX")
    throw new Exception($"label table should win over the pack's own zh: {mtxZh.Label} | {mtxZh.LabelSecondary}");

languageBox.SelectedItem = ItemById(languageBox.ItemsSource, "en");
Dispatcher.UIThread.RunJobs();
var fireEn = groups.Single(g => g.Group == "skills").Categories.Single(c => c.Id == "skill.fire");
if (fireEn.Label != "Fire" || fireEn.LabelSecondary != "火焰" || fireEn.CountText != "1 files")
    throw new Exception($"en should lead with the English name: {fireEn.Label} | {fireEn.LabelSecondary} | {fireEn.CountText}");
languageBox.SelectedItem = ItemById(languageBox.ItemsSource, "zh");
Dispatcher.UIThread.RunJobs();

// The search box matches both languages whichever one is primary.
// The pack says 迷雾 (simplified, written in 2026-11) while the shipped table renders 迷霧 — both must find the row.
searchBox.Text = "迷雾";
Dispatcher.UIThread.RunJobs();
if (groups.Count != 1 || groups[0].Group != "leagues")
    throw new Exception($"searching in Chinese failed: {groups.Count} groups");
searchBox.Text = "Delirium";
Dispatcher.UIThread.RunJobs();
if (groups.Count != 1 || groups[0].Group != "leagues")
    throw new Exception($"searching in English while zh is selected failed: {groups.Count} groups");
searchBox.Text = "";
Dispatcher.UIThread.RunJobs();

searchBox.Text = "fire";
Dispatcher.UIThread.RunJobs();
if (groups.Count != 1 || groups[0].Group != "skills")
    throw new Exception($"search by name failed: {groups.Count}");

searchBox.Text = "fireball";
Dispatcher.UIThread.RunJobs();
if (groups.Count != 1 || groups[0].Categories.Single().Id != "skill.fire")
    throw new Exception("search by path failed");

searchBox.Text = "";
groupFilter.SelectedIndex = 1; // skills
Dispatcher.UIThread.RunJobs();
if (groups.Count != 1 || groups[0].Group != "skills" || groups[0].Categories.Count != 1)
    throw new Exception($"group filter failed: {groups.Count}");

searchBox.Text = "zzz-no-match";
Dispatcher.UIThread.RunJobs();
if (groups.Count != 0 || !emptyText.IsVisible)
    throw new Exception("empty state failed");

// ---------------------------------------------------------------------------
// Preset seeding, tri-state partial, and zero-match / unknown-preset warnings
// ---------------------------------------------------------------------------
searchBox.Text = "";
groupFilter.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();

var presetBox = window.FindControl<ComboBox>("PresetBox") ?? throw new Exception("PresetBox missing");
var statusText = window.FindControl<TextBlock>("StatusText") ?? throw new Exception("StatusText missing");
var selectionText = window.FindControl<TextBlock>("SelectionText") ?? throw new Exception("SelectionText missing");
var applyButton = window.FindControl<Button>("ApplyButton") ?? throw new Exception("ApplyButton missing");
var refreshStatus = typeof(MainWindow).GetMethod("RefreshStatus",
    BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("RefreshStatus not found");

// fake game so RefreshStatus passes its no-game early-return and re-evaluates the Apply gate
var gameField = typeof(MainWindow).GetField("_game", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("_game field not found");
gameField.SetValue(window, new PsCore.GameLocation(
    Path.Combine(home, "fakegame"),
    Path.Combine(home, "fakegame", "Bundles2"),
    Path.Combine(home, "fakegame", "Bundles2", "_.index.bin")));

// partial manual change flips a multi-category group checkbox to three-state
var mtxRow = groups.Single(g => g.Group == "mtx");
if (mtxRow.Categories.Count != 2 || mtxRow.IsSelected != true)
    throw new Exception($"mtx group should have 2 checked categories: {mtxRow.Categories.Count}");
mtxRow.Categories[0].IsSelected = false;
Dispatcher.UIThread.RunJobs();
if (mtxRow.IsSelected != null)
    throw new Exception("group checkbox should be partial (null) after unchecking one category");
mtxRow.Categories[0].IsSelected = true;
Dispatcher.UIThread.RunJobs();
if (mtxRow.IsSelected != true)
    throw new Exception("group checkbox should recover to checked");

// switching preset re-seeds every group
presetBox.SelectedItem = ItemById(presetBox.ItemsSource, "performance");
Dispatcher.UIThread.RunJobs();
groups = (ObservableCollection<CategoryGroupRow>)categoryGroupList.ItemsSource!;
if (groups.Single(g => g.Group == "leagues").IsSelected != true ||
    groups.Single(g => g.Group == "skills").IsSelected != false)
    throw new Exception("performance preset should re-seed selections (leagues on, skills off)");

// pack whose ids match no preset selector: unknown preset name must warn, not fail silently
var foreignAll = Path.Combine(home, "rulepacks", "foreign-all.json");
RulePackStore.Save(new RulePack
{
    Name = "Foreign All",
    Categories =
    [
        new RuleCategory
        {
            Id = "fx.spells.fire",
            Group = "misc",
            Label = new() { ["en"] = "Fx" },
            Rules = [new Rule { Path = "fx/fireball.ao", Strategy = StrategyKind.StripAttachments }]
        },
        // what the builder wrote before it framed the zh slot: English in both places
        new RuleCategory
        {
            Id = "skill.zzz_stale",
            Group = "skills",
            Label = new() { ["zh"] = "Old Name", ["en"] = "Old Name" },
            Rules = [new Rule { Path = "metadata/effects/spells/zzz/a.ao", Strategy = StrategyKind.StripAttachments }]
        }
    ],
    Presets = new() { ["full"] = ["*"] }
}, foreignAll);
loadPack.Invoke(window, [foreignAll]);
refreshStatus.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (!statusText.Text!.Contains("不在规则包") || !statusText.Text.Contains("not found"))
    throw new Exception($"unknown preset name should warn: {statusText.Text}");
if (!selectionText.Text!.Contains("已选 2/2"))
    throw new Exception($"unknown preset should fall back to all-selected: {selectionText.Text}");
if (!applyButton.IsEnabled)
    throw new Exception("apply should stay enabled while rows are selected");

// A pack with no zh at all must show the name once, not "Fx / Fx".
var foreignRow = groups.SelectMany(g => g.Categories).Single(c => c.Id == "fx.spells.fire");
if (foreignRow.Label != "Fx" || foreignRow.LabelSecondary != null || foreignRow.FullLabel != "Fx")
    throw new Exception($"english-only pack should not repeat itself: {foreignRow.FullLabel}");

// The row a 2026-11 pack really has: English in the zh slot. It must arrive framed, not bare —
// that is the whole reason framing lives in LabelFor instead of only in the builder.
var staleRow = groups.SelectMany(g => g.Categories).Single(c => c.Id == "skill.zzz_stale");
if (staleRow.Label != "技能: Old Name" || staleRow.LabelSecondary != null)
    throw new Exception($"stale pack row should be framed without repeating: {staleRow.FullLabel}");

// known preset that matches zero categories: amber warning + Apply disabled + honest count
var foreignSafe = Path.Combine(home, "rulepacks", "foreign-safe.json");
RulePackStore.Save(new RulePack
{
    Name = "Foreign Safe",
    Categories =
    [
        new RuleCategory
        {
            Id = "fx.spells.fire",
            Group = "misc",
            Label = new() { ["en"] = "Fx" },
            Rules = [new Rule { Path = "fx/fireball.ao", Strategy = StrategyKind.StripAttachments }]
        }
    ],
    Presets = new() { ["safe"] = ["skill.*", "mtx.*"], ["full"] = ["*"] }
}, foreignSafe);
presetBox.SelectedItem = ItemById(presetBox.ItemsSource, "safe");
Dispatcher.UIThread.RunJobs();
loadPack.Invoke(window, [foreignSafe]);
refreshStatus.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (!statusText.Text!.Contains("未匹配任何类别") && !statusText.Text.Contains("matched 0"))
    throw new Exception($"zero-match preset should warn: {statusText.Text}");
if (!selectionText.Text!.Contains("已选 0/1"))
    throw new Exception($"selection text should show 0/1: {selectionText.Text}");
if (applyButton.IsEnabled)
    throw new Exception("apply must be disabled when the preset matches nothing");

// original pack still loads cleanly afterwards
loadPack.Invoke(window, [packPath]);
refreshStatus.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (!selectionText.Text!.Contains("已选 3/4"))
    throw new Exception($"safe should re-select 3 of 4: {selectionText.Text}");

// ---------------------------------------------------------------------------
// Sponsor tab: sits last, right after Settings, and its embedded QR resolves
// ---------------------------------------------------------------------------
var tabControl = window.GetLogicalDescendants().OfType<TabControl>().Single();
var tabs = tabControl.Items.OfType<TabItem>().ToList();
if (tabs.Count != 6)
    throw new Exception($"expected 6 tabs, got {tabs.Count}");
if (tabs[^1].Header is not string sponsorHeader || !sponsorHeader.Contains("赞助"))
    throw new Exception($"last tab should be the sponsor tab: {tabs[^1].Header}");
if (tabs[^2].Header is not string settingsHeader || !settingsHeader.Contains("设置"))
    throw new Exception($"sponsor tab must follow settings: {tabs[^2].Header}");

tabControl.SelectedItem = tabs[^1];
Dispatcher.UIThread.RunJobs();

// A bad avares:// URI throws while the window's XAML loads, so reaching here with
// a set Source means Assets/sponsor-alipay.jpg is embedded (AvaloniaResource item).
// The headless platform decodes every bitmap as 1x1, so pixel size is not assertable.
var images = window.GetLogicalDescendants().OfType<Image>().ToList();
if (images.Count != 1)
    throw new Exception($"expected exactly one image (the sponsor QR), got {images.Count}");
if (images[0].Source == null)
    throw new Exception("sponsor QR did not load from avares://");

// ---------------------------------------------------------------------------
// Windows / standalone support: browse button, elevate button, target kind line
// ---------------------------------------------------------------------------
var browseButton = window.FindControl<Button>("BrowseButton") ?? throw new Exception("BrowseButton missing");
var elevateButton = window.FindControl<Button>("ElevateButton") ?? throw new Exception("ElevateButton missing");
var kindText = window.FindControl<TextBlock>("KindText") ?? throw new Exception("KindText missing");
if (elevateButton.IsVisible)
    throw new Exception("elevate button must stay hidden until a write is denied");
if (browseButton.Content?.ToString()?.Contains("Browse") != true)
    throw new Exception($"browse button label: {browseButton.Content}");

var updateKind = typeof(MainWindow).GetMethod("UpdateKindText",
    BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("UpdateKindText not found");

gameField.SetValue(window, new PsCore.GameLocation(
    Path.Combine(home, "standalone"),
    "",
    "",
    null,
    PsCore.GameLocationKind.StandaloneGgpk,
    Path.Combine(home, "standalone", "Content.ggpk")));
updateKind.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (kindText.Text?.Contains("实验性") != true || kindText.Text?.Contains("Content.ggpk") != true)
    throw new Exception($"standalone kind hint missing: {kindText.Text}");

gameField.SetValue(window, new PsCore.GameLocation(
    Path.Combine(home, "fakegame"),
    Path.Combine(home, "fakegame", "Bundles2"),
    Path.Combine(home, "fakegame", "Bundles2", "_.index.bin")));
updateKind.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (kindText.Text?.Contains("Steam") != true)
    throw new Exception($"steam kind hint missing: {kindText.Text}");

// ---------------------------------------------------------------------------
// 勾选实验性 → apply → restore: the whole loop through the real checkbox and buttons
// ---------------------------------------------------------------------------
{
    // A real, writable synthetic client: one stable .ao rule plus the two blanking strategies
    // (.pet/.mat) that the experimental checkbox gates.
    var loopRoot = PsTests.TestIndexBuilder.NewRoot("ui-experimental");
    const string loopAoPath = "metadata/effects/spells/fire/fireball.ao";
    const string loopPetPath = "metadata/effects/spells/fire/burn.pet";
    const string loopMatPath = "metadata/effects/spells/fire/shield.mat";
    var loopOriginalAo = "version 1\nAddAttached( 'metadata/effects/spells/fire/burn.pet', <root> );\nKeepThis();\n";
    // Both originals are deliberately bigger than any blank template, so "the template was written"
    // can never be satisfied by a file that was never touched.
    var loopPetBytes = PsTests.BlankAssert.Fixture(loopPetPath);
    var loopMatBytes = PsTests.BlankAssert.Fixture(loopMatPath);
    var loopBundles = Path.Combine(loopRoot, "Bundles2");
    var loopIndex = PsTests.TestIndexBuilder.Build(loopBundles, new (string, byte[])[]
    {
        (loopAoPath, Encoding.UTF8.GetBytes(loopOriginalAo)),
        (loopPetPath, loopPetBytes),
        (loopMatPath, loopMatBytes)
    });
    var loopPackPath = Path.Combine(home, "rulepacks", "ui-experimental.json");
    var (loopPack, loopPackStats) = RulePackBuilder.Build(
        [loopAoPath, loopPetPath, loopMatPath], "UI Experimental", "test");
    if (loopPackStats.Rules != 3 || loopPack.Categories.Sum(c => c.Rules.Count(r => r.Experimental)) != 2)
        throw new Exception($"fixture should carry 3 rules / 2 experimental: {loopPackStats.Rules}");
    RulePackStore.Save(loopPack, loopPackPath);

    var loopGame = PsCore.SteamLibraryLocator.FromIndex(loopIndex);
    var loopWindow = new MainWindow();
    var loopTier = loopWindow.FindControl<ComboBox>("TierBox")
                   ?? throw new Exception("TierBox missing");
    // Selecting through the real combo box runs OnTierChanged, so this is what a user does —
    // not just poking a field. Labels come from TierItem.For, the same source the window builds.
    void SetTier(RiskTier tier)
    {
        loopTier.SelectedItem = TierItem.For(tier);
        Dispatcher.UIThread.RunJobs();
    }
    RiskTier CurrentTier() => (loopTier.SelectedItem as TierItem)?.Tier ?? RiskTier.Safe;
    var loopApply = loopWindow.FindControl<Button>("ApplyButton") ?? throw new Exception("ApplyButton missing");
    var loopRestore = loopWindow.FindControl<Button>("RestoreButton") ?? throw new Exception("RestoreButton missing");
    var loopStatus = loopWindow.FindControl<TextBlock>("StatusText") ?? throw new Exception("StatusText missing");
    var loopState = loopWindow.FindControl<TextBlock>("StateText") ?? throw new Exception("StateText missing");
    var loopLog = loopWindow.FindControl<TextBox>("LogBox") ?? throw new Exception("LogBox missing");
    var loopStateFile = Path.Combine(home, "state.json");

    gameField.SetValue(loopWindow, loopGame);
    loadPack.Invoke(loopWindow, [loopPackPath]);
    refreshStatus.Invoke(loopWindow, null);
    Dispatcher.UIThread.RunJobs();

    if (CurrentTier() != RiskTier.Safe)
        throw new Exception($"a fresh window must start on the safe tier, got {CurrentTier()}");
    if (File.Exists(loopStateFile))
        throw new Exception("the loop needs a clean state file to start from");

    // Apply/Restore are async void handlers: the click arms the button, the second click runs
    // the work on a background task and reports through StatusText. Pump the dispatcher until
    // a terminal message lands, then assert against the bytes actually written to the index.
    void ClickTwice(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    void PumpUntil(Func<bool> settled, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!settled())
        {
            if (DateTime.UtcNow > deadline)
                throw new Exception($"GUI {what} timed out at: {loopStatus.Text}");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    bool ApplySettled() => loopStatus.Text is { } t &&
                           (t.Contains("应用完成") || t.Contains("没有生效") || t.Contains("应用失败"));
    bool RestoreSettled() => loopStatus.Text is { } t &&
                             (t.Contains("还原完成") || t.Contains("还原失败"));

    byte[] LiveBytes(string path)
    {
        using var read = PsCore.IndexSession.OpenReadOnly(loopGame);
        read.TryGetFile(path, out var record);
        return record!.Read().ToArray();
    }

    // Safe tier: only the .ao is suppressed, and both blanking rules have to be reported —
    // split by tier, so a 50k .mat class can't hide inside one "experimental" number.
    ClickTwice(loopApply);
    PumpUntil(ApplySettled, "safe apply");
    if (loopStatus.Text!.Contains("应用完成") != true)
        throw new Exception($"safe apply failed: {loopStatus.Text}");
    if (!loopStatus.Text.Contains("1/3 active") ||
        !loopStatus.Text.Contains("实验 2 / experimental") ||
        !loopStatus.Text.Contains("特效 1") || !loopStatus.Text.Contains("材质 1"))
        throw new Exception($"skip buckets not reported per tier while on safe: {loopStatus.Text}");
    if (loopLog.Text!.Contains("skipped at tier") != true)
        throw new Exception($"loading a pack with blanking rules should hint at the tier box: {loopLog.Text}");
    if (LiveBytes(loopPetPath).SequenceEqual(loopPetBytes) != true)
        throw new Exception(".pet must stay untouched on the safe tier");
    if (LiveBytes(loopMatPath).SequenceEqual(loopMatBytes) != true)
        throw new Exception(".mat must stay untouched on the safe tier");

    // fx tier: the particle file blanks, the material still does not. This split is the whole
    // point of the tier box; the old checkbox could not express it at all.
    SetTier(RiskTier.Fx);
    ClickTwice(loopApply);
    PumpUntil(ApplySettled, "fx apply");
    if (loopStatus.Text!.Contains("应用完成") != true)
        throw new Exception($"fx apply failed: {loopStatus.Text}");
    if (!PsTests.BlankAssert.IsTemplated(loopPetPath, LiveBytes(loopPetPath)))
        throw new Exception("fx tier must blank the .pet with its template");
    if (LiveBytes(loopMatPath).SequenceEqual(loopMatBytes) != true)
        throw new Exception("fx tier must leave the .mat alone");
    if (!loopStatus.Text.Contains("材质 1"))
        throw new Exception($"fx apply should still report the one material rule it skipped: {loopStatus.Text}");

    ClickTwice(loopRestore);
    PumpUntil(RestoreSettled, "fx restore");

    // Material tier: one click opens the in-window confirmation instead of writing, and only
    // the explicit confirm button performs the write (it replaces the four-second arm).
    SetTier(RiskTier.Material);
    loopApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var loopConfirmPanel = loopWindow.FindControl<Border>("MaterialConfirmPanel")
                           ?? throw new Exception("MaterialConfirmPanel missing");
    var loopConfirm = loopWindow.FindControl<Button>("MaterialConfirmButton")
                      ?? throw new Exception("MaterialConfirmButton missing");
    var loopConfirmText = loopWindow.FindControl<TextBlock>("MaterialConfirmText")
                          ?? throw new Exception("MaterialConfirmText missing");
    PumpUntil(() => loopConfirmPanel.IsVisible == true, "material confirmation panel");
    if (loopConfirmText.Text!.Contains("1 个 .mat") != true)
        throw new Exception($"the panel must name what it is about to destroy: {loopConfirmText.Text}");

    loopConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    PumpUntil(ApplySettled, "material apply");
    if (loopStatus.Text!.Contains("应用完成") != true)
        throw new Exception($"material apply failed: {loopStatus.Text}");
    if (!loopStatus.Text.Contains("3/3 active") || !loopStatus.Text.Contains("实验 0 / experimental"))
        throw new Exception($"the material tier should move both skipped rules into the change set: {loopStatus.Text}");
    if (!PsTests.BlankAssert.IsTemplated(loopPetPath, LiveBytes(loopPetPath)))
        throw new Exception("material apply must blank the .pet");
    if (!PsTests.BlankAssert.IsTemplated(loopMatPath, LiveBytes(loopMatPath)))
        throw new Exception("material apply must blank the .mat");
    if (Encoding.UTF8.GetString(LiveBytes(loopAoPath)).Contains("AddAttached"))
        throw new Exception("material apply must keep suppressing the .ao");
    if (!File.Exists(loopStateFile))
        throw new Exception("apply must leave a state file behind");

    // The flagship scenario, through the UI: the materials were the mistake, the rest is wanted.
    // A scoped restore must give the .mat files back and leave the script surgery and the blanked
    // particle file exactly where they were — the old build could only undo all of it or none.
    var loopScopeBox = loopWindow.FindControl<ComboBox>("RestoreScopeBox")
                       ?? throw new Exception("RestoreScopeBox missing");
    var materialOnly = loopScopeBox.Items.OfType<RestoreScopeItem>()
        .First(item => item.Tiers is { Count: 1 } only && only.Contains(RiskTier.Material));
    loopScopeBox.SelectedItem = materialOnly;
    Dispatcher.UIThread.RunJobs();

    ClickTwice(loopRestore);
    PumpUntil(RestoreSettled, "scoped restore");
    if (loopStatus.Text!.Contains("还原完成") != true)
        throw new Exception($"scoped restore failed: {loopStatus.Text}");
    if (LiveBytes(loopMatPath).SequenceEqual(loopMatBytes) != true)
        throw new Exception("a material-scoped restore must give the .mat bytes back");
    if (!PsTests.BlankAssert.IsTemplated(loopPetPath, LiveBytes(loopPetPath)))
        throw new Exception("a material-scoped restore must leave the blanked .pet alone");
    if (Encoding.UTF8.GetString(LiveBytes(loopAoPath)).Contains("AddAttached"))
        throw new Exception("a material-scoped restore must leave the .ao surgery in place");
    if (!File.Exists(loopStateFile))
        throw new Exception("the kept suppressions must still be tracked after a scoped restore");
    if (loopState.Text!.Contains("原始 / original") == true)
        throw new Exception($"status must not claim an original client after a partial restore: {loopState.Text}");

    // Back to restoring everything: the remainder has to go back too, and the state file with it.
    loopScopeBox.SelectedItem = loopScopeBox.Items.OfType<RestoreScopeItem>().First(item => item.Tiers == null);
    Dispatcher.UIThread.RunJobs();
    ClickTwice(loopRestore);
    PumpUntil(RestoreSettled, "restore");
    if (loopStatus.Text!.Contains("还原完成") != true)
        throw new Exception($"restore failed: {loopStatus.Text}");
    if (File.Exists(loopStateFile))
        throw new Exception("restore must clear the state file");
    if (LiveBytes(loopPetPath).SequenceEqual(loopPetBytes) != true)
        throw new Exception("restored .pet must have its original bytes");
    if (LiveBytes(loopMatPath).SequenceEqual(loopMatBytes) != true)
        throw new Exception("restored .mat must have its original bytes");
    if (Encoding.UTF8.GetString(LiveBytes(loopAoPath)) != loopOriginalAo)
        throw new Exception("restored .ao must have its original script");
    if (loopState.Text!.Contains("原始 / original") != true)
        throw new Exception($"status should read original after restore: {loopState.Text}");

    // The tier is a preference: it has to survive a restart, serialized as a string (these option
    // objects have no JsonStringEnumConverter, so an enum property would land as an integer),
    // and the legacy boolean must stay mirrored for older builds.
    var saveSettings = typeof(MainWindow).GetMethod("SaveSettings",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("SaveSettings not found");
    saveSettings.Invoke(loopWindow, null);
    var savedSettings = File.ReadAllText(Path.Combine(home, "settings.json"));
    if (!savedSettings.Contains("\"ExperimentalTier\": \"material\""))
        throw new Exception($"the tier must be persisted as a string: {savedSettings}");
    if (!savedSettings.Contains("\"IncludeExperimental\": true"))
        throw new Exception($"the legacy boolean must be mirrored: {savedSettings}");
    var reopened = new MainWindow();
    var reopenedTier = reopened.FindControl<ComboBox>("TierBox")
                       ?? throw new Exception("TierBox missing on a reopened window");
    if ((reopenedTier.SelectedItem as TierItem)?.Tier != RiskTier.Material)
        throw new Exception("a reopened window must restore the material tier");
    if (File.ReadAllText(Path.Combine(home, "settings.json")) != savedSettings)
        throw new Exception("opening a window must not rewrite the saved settings");

    // A profile carries the tier too: 存档案 → 降档 → 载入档案 must put it back.
    var loopProfileName = loopWindow.FindControl<TextBox>("ProfileNameBox")
                          ?? throw new Exception("ProfileNameBox missing");
    var saveProfile = typeof(MainWindow).GetMethod("OnSaveProfile",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("OnSaveProfile not found");
    var loadProfile = typeof(MainWindow).GetMethod("OnLoadProfile",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("OnLoadProfile not found");

    SetTier(RiskTier.Material);
    loopProfileName.Text = "ui-experimental";
    saveProfile.Invoke(loopWindow, new object?[] { null, null });
    SetTier(RiskTier.Safe);
    loadProfile.Invoke(loopWindow, new object?[] { null, null });
    Dispatcher.UIThread.RunJobs();
    if (CurrentTier() != RiskTier.Material)
        throw new Exception("loading a profile must restore its saved tier");

    // …and dropping the tier back has to persist too — a reopened window must not stay on material.
    SetTier(RiskTier.Safe);
    saveSettings.Invoke(loopWindow, null);
    if ((new MainWindow()).FindControl<ComboBox>("TierBox") is { SelectedItem: TierItem reopenedSafe } &&
        reopenedSafe.Tier != RiskTier.Safe)
        throw new Exception("a reopened window must restore the safe tier");

    // Leaving the tier on safe after the loop means a later apply still never touches those files.
    ClickTwice(loopApply);
    PumpUntil(ApplySettled, "re-apply after restore");
    if (loopStatus.Text!.Contains("1/3 active") != true)
        throw new Exception($"re-applying unticked should skip both blanking rules again: {loopStatus.Text}");
    if (LiveBytes(loopPetPath).SequenceEqual(loopPetBytes) != true)
        throw new Exception("re-applied unticked run must leave the .pet alone");
    ClickTwice(loopRestore);
    PumpUntil(RestoreSettled, "final restore");
    if (LiveBytes(loopAoPath).SequenceEqual(Encoding.UTF8.GetBytes(loopOriginalAo)) != true)
        throw new Exception("the loop must end on the original client bytes");

    // The "write strategy" box is the escape hatch back to the old 0-byte behaviour. A fresh window
    // must start on templates, and the choice has to survive both a save and a restart.
    var loopStyleBox = loopWindow.FindControl<ComboBox>("BlankStyleBox")
                       ?? throw new Exception("BlankStyleBox missing");
    WriteStrategyItem PickStrategy(WriteStrategy want) =>
        loopStyleBox.Items.OfType<WriteStrategyItem>().First(i => i.Strategy == want);
    if (loopStyleBox.Items.OfType<WriteStrategyItem>().Count() != 5)
        throw new Exception("the write-strategy box must offer all five strategies");
    if ((loopStyleBox.SelectedItem as WriteStrategyItem)?.Strategy != WriteStrategy.Template)
        throw new Exception("a window must default to writing valid empty templates");
    if ((new MainWindow()).FindControl<ComboBox>("BlankStyleBox") is not { SelectedItem: WriteStrategyItem fresh }
        || fresh.Strategy != WriteStrategy.Template)
        throw new Exception("a reopened window must default to the template style too");
    // The compression toggles only appear for a compression-containing strategy.
    if (loopWindow.FindControl<Border>("CompressionPanel")!.IsVisible)
        throw new Exception("compression panel must be hidden for the template strategy");

    loopStyleBox.SelectedItem = PickStrategy(WriteStrategy.Empty);
    Dispatcher.UIThread.RunJobs();
    SetTier(RiskTier.Material);
    loopApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    PumpUntil(() => loopWindow.FindControl<Border>("MaterialConfirmPanel")!.IsVisible == true, "material panel (empty style)");
    loopWindow.FindControl<Button>("MaterialConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    PumpUntil(ApplySettled, "empty-style apply");
    if (LiveBytes(loopPetPath).Length != 0)
        throw new Exception("--blank-style empty must still write a 0-byte file");
    saveSettings.Invoke(loopWindow, null);
    var styleSettings = File.ReadAllText(Path.Combine(home, "settings.json"));
    if (!styleSettings.Contains("\"BlankStyle\": \"empty\"") || !styleSettings.Contains("\"WriteStrategy\": \"empty\""))
        throw new Exception($"the write strategy and its legacy mirror must persist: {styleSettings}");
    if ((new MainWindow()).FindControl<ComboBox>("BlankStyleBox") is not { SelectedItem: WriteStrategyItem reopenedStyle }
        || reopenedStyle.Strategy != WriteStrategy.Empty)
        throw new Exception("a reopened window must restore the empty-byte style");

    // A compression-containing strategy reveals the compression panel; a blanking one hides it.
    loopStyleBox.SelectedItem = PickStrategy(WriteStrategy.CompressThenTemplate);
    Dispatcher.UIThread.RunJobs();
    if (!loopWindow.FindControl<Border>("CompressionPanel")!.IsVisible)
        throw new Exception("compression panel must appear for a compress-then strategy");
    loopStyleBox.SelectedItem = PickStrategy(WriteStrategy.CompressionOnly);
    Dispatcher.UIThread.RunJobs();
    if (!loopWindow.FindControl<Border>("CompressionPanel")!.IsVisible)
        throw new Exception("compression panel must appear for compress-only");

    // Back to templates and everything clean again, so the loop still ends on original bytes.
    loopStyleBox.SelectedItem = PickStrategy(WriteStrategy.Template);
    if (loopWindow.FindControl<Border>("CompressionPanel")!.IsVisible)
        throw new Exception("compression panel must hide again for the template strategy");
    Dispatcher.UIThread.RunJobs();
    ClickTwice(loopRestore);
    PumpUntil(RestoreSettled, "restore after empty style");
    SetTier(RiskTier.Safe);
    if (LiveBytes(loopPetPath).SequenceEqual(loopPetBytes) != true)
        throw new Exception("the loop must still end on the original .pet bytes");
    if (LiveBytes(loopAoPath).SequenceEqual(Encoding.UTF8.GetBytes(loopOriginalAo)) != true)
        throw new Exception("the loop must end on the original client bytes");
}

// ---------------------------------------------------------------------------
// 地面特效树(Ignore 页左栏):勾 → 忽略列表文本 → 磁盘 → 重启
// ---------------------------------------------------------------------------
const string groundV3 = GroundEffectProtection.GroundEffectsV3;
var groundPackPath = Path.Combine(home, "rulepacks", "ground.json");
RulePackStore.Save(new RulePack
{
    Name = "Ground UI",
    Categories =
    [
        new RuleCategory
        {
            Id = "skill.ground_effects",
            Group = "skills",
            Label = new() { ["zh"] = "地面效果特效", ["en"] = "Ground Effects" },
            Rules =
            [
                new Rule { Path = "metadata/effects/spells/ground_effects/holy/ground_01.ao", Strategy = StrategyKind.StripAttachments },
                new Rule { Path = "metadata/effects/spells/ground_effects/invisground/rig.ao", Strategy = StrategyKind.StripAttachments }
            ]
        },
        new RuleCategory
        {
            Id = "skill.ground_effects_v3",
            Group = "skills",
            Label = new() { ["zh"] = "地面效果三版", ["en"] = "Ground Effects V3" },
            Rules =
            [
                new Rule { Path = groundV3 + "/brittle/brittle_grd.ao", Strategy = StrategyKind.StripAttachments },
                new Rule { Path = groundV3 + "/brittle/fx/brittle.pet", Strategy = StrategyKind.NullPet },
                new Rule { Path = groundV3 + "/profane/profane_epk_pass0.mat", Strategy = StrategyKind.NullMaterial }
            ]
        },
        // 名字里也带 ground,但它是一个技能的特效,不是"地面效果":不该出现在这棵树里。
        new RuleCategory
        {
            Id = "skill.ground_slam_new",
            Group = "skills",
            Label = new() { ["zh"] = "裂地重擊(新版)", ["en"] = "Ground Slam" },
            Rules = [new Rule { Path = "metadata/effects/spells/ground_slam_new/groundspine_01.ao", Strategy = StrategyKind.StripAttachments }]
        }
    ],
    Presets = new()
    {
        ["safe"] = ["skill.*", "mtx.*"],
        ["clean"] = ["skill.*", "mtx.*", "league.*"],
        ["performance"] = ["league.*"],
        ["full"] = ["*"]
    }
}, groundPackPath);

// A window with no rule pack loaded cannot list folders it has never seen: the tree's empty state
// has to say so instead of showing a box of checkboxes that protects nothing.
var bareWindow = new MainWindow();
if (bareWindow.FindControl<TextBlock>("GroundEmptyText") is not { IsVisible: true })
    throw new Exception("a window without a rule pack must explain why the ground tree is empty");

loadPack.Invoke(window, new object[] { groundPackPath });
Dispatcher.UIThread.RunJobs();

var groundList = window.FindControl<ItemsControl>("GroundBranchList")
                 ?? throw new Exception("GroundBranchList missing");
var groundRows = groundList.ItemsSource as ObservableCollection<GroundRow>
                 ?? throw new Exception("the ground tree is not bound to its rows");
if (groundRows.Count != 2 || groundRows.Sum(r => r.Leaves.Count) != 4)
    throw new Exception($"the tree must show the two ground categories and their four folders, got " +
                        $"{groundRows.Count} branches / {groundRows.Sum(r => r.Leaves.Count)} leaves");
if (groundRows.Any(r => r.Name.Contains("Slam") || r.Leaves.Any(l => l.Prefix.Contains("ground_slam"))))
    throw new Exception("ground_slam is a skill, not a ground effect — it must not be in the tree");

// Names: shipped official label with the folder name beside it (the folder name is what lands in
// ignore.txt, so hiding it would make the two halves of the tab unreadable against each other), and
// the folder name alone where the table deliberately has no entry.
var holyLeaf = groundRows.SelectMany(r => r.Leaves).First(l => l.Prefix.EndsWith("/holy", StringComparison.Ordinal));
if (holyLeaf.Name != "奉獻地面" || holyLeaf.NameSecondary != "holy")
    throw new Exception($"the holy folder must read 奉獻地面 / holy, got {holyLeaf.Name} / {holyLeaf.NameSecondary}");
var invisLeaf = groundRows.SelectMany(r => r.Leaves).First(l => l.Prefix.EndsWith("/invisground", StringComparison.Ordinal));
if (invisLeaf.Name != "invisground" || invisLeaf.HasSecondary)
    throw new Exception($"a folder with no official name must show its own name, got {invisLeaf.Name}");
var brittleLeaf = groundRows.SelectMany(r => r.Leaves).First(l => l.Prefix.EndsWith("/brittle", StringComparison.Ordinal));
if (!brittleLeaf.CountText.Contains("2"))
    throw new Exception($"the brittle folder carries two files: {brittleLeaf.CountText}");

// Ticking a branch writes ONE line and every folder under it reads as protected-but-not-separate:
// un-ticking a child would mean deleting the parent's line, which is not what a click on the child
// can honestly do. SetGroundProtection is the single entry point the tree's Click handler calls.
var setProtection = typeof(MainWindow).GetMethod("SetGroundProtection",
    BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("SetGroundProtection not found");
var groundIgnoreBox = window.FindControl<TextBox>("IgnoreBox") ?? throw new Exception("IgnoreBox missing");
var v3Branch = groundRows.First(r => r.Prefix == groundV3);
// Earlier sections of this file really apply and really save the list, so the file may exist with
// unrelated content; what a tick must not do is touch it.
var ignoreFile = Path.Combine(home, "ignore.txt");
string IgnoreOnDisk() => File.Exists(ignoreFile) ? File.ReadAllText(ignoreFile) : "";
var diskBeforeTick = IgnoreOnDisk();
setProtection.Invoke(window, new object[] { new List<string> { groundV3 }, true });
Dispatcher.UIThread.RunJobs();
if (!groundIgnoreBox.Text!.Contains(groundV3))
    throw new Exception($"ticking a branch must write its line: {groundIgnoreBox.Text}");
if (!v3Branch.IsEnabled || v3Branch.IsChecked != true)
    throw new Exception("a branch with a line of its own stays ticked and enabled");
if (brittleLeaf.IsChecked != true || brittleLeaf.IsEnabled)
    throw new Exception($"a folder covered by its branch must read ticked+disabled, got {brittleLeaf.IsChecked}/{brittleLeaf.IsEnabled}");
if (invisLeaf.IsEnabled != true || invisLeaf.IsChecked == true)
    throw new Exception("the other category is untouched and must stay tickable");

// The tick is only text until 保存 (or an apply) puts it on disk — same rule as editing the box.
if (IgnoreOnDisk() != diskBeforeTick)
    throw new Exception("ticking a tree row must not write ignore.txt by itself");
var saveIgnore = typeof(MainWindow).GetMethod("OnSaveIgnore",
    BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("OnSaveIgnore not found");
saveIgnore.Invoke(window, new object?[] { null, null });
if (!File.ReadAllText(ignoreFile).Contains(groundV3))
    throw new Exception("saving the list must carry the ground line to disk");

// The restart round-trip: the tree has no storage of its own, so a reopened window reads the file
// back and shows the same tick — and opening it must still not rewrite settings.json.
var settingsBefore = File.ReadAllText(Path.Combine(home, "settings.json"));
var reopenedGround = new MainWindow();
var reopenedBox = reopenedGround.FindControl<TextBox>("IgnoreBox")
                  ?? throw new Exception("IgnoreBox missing on a reopened window");
if (!reopenedBox.Text!.Contains(groundV3))
    throw new Exception("a reopened window must load the ignore list from disk");
loadPack.Invoke(reopenedGround, new object[] { groundPackPath });
Dispatcher.UIThread.RunJobs();
var reopenedRows = (reopenedGround.FindControl<ItemsControl>("GroundBranchList")!.ItemsSource as ObservableCollection<GroundRow>)!;
var reopenedV3 = reopenedRows.First(r => r.Prefix == groundV3);
if (reopenedV3.IsChecked != true)
    throw new Exception("the ground tree must come back ticked after a restart");
if (File.ReadAllText(Path.Combine(home, "settings.json")) != settingsBefore)
    throw new Exception("restoring the ignore list at startup must not rewrite the settings file");

// Un-tick the top node: it owns no single line (…/ground_effects does not reach …/_v2), so one
// click has to remove all three category lines.
setProtection.Invoke(window, new object[] { GroundEffectProtection.CategoryPrefixes.ToList(), false });
Dispatcher.UIThread.RunJobs();
if (groundIgnoreBox.Text!.Contains("ground_effects"))
    throw new Exception($"un-ticking the top node must clear every ground line: {groundIgnoreBox.Text}");
if (v3Branch.IsChecked == true)
    throw new Exception("the branch must read un-ticked once its line is gone");

// ---------------------------------------------------------------------------
// 遗留 bundle 面板:状态文件丢了以后,GUI 里那条死路的出口
// ---------------------------------------------------------------------------
{
    var orphanRoot = PsTests.TestIndexBuilder.NewRoot("ui-orphan");
    const string orphanAoPath = "metadata/effects/spells/fire/fireball.ao";
    var orphanIndex = PsTests.TestIndexBuilder.Build(Path.Combine(orphanRoot, "Bundles2"),
        new (string, byte[])[]
        {
            (orphanAoPath, Encoding.UTF8.GetBytes("version 1\nAddAttached( 'x' );\nKeepThis();\n"))
        });
    var orphanBundles = Path.Combine(orphanRoot, "Bundles2");
    var orphanGame = new PsCore.GameLocation(orphanRoot, orphanBundles, orphanIndex);
    var orphanService = new PsCore.AppService(new PsCore.AppPaths());   // same POE2SMOOTHER_HOME as the window
    var (orphanPack, _) = orphanService.BuildRules(orphanGame, "Orphan UI", "test");
    var orphanApplied = orphanService.Apply(new PsCore.ApplyRequest
    {
        Game = orphanGame,
        Pack = orphanPack,
        Preset = "safe"
    });

    gameField.SetValue(window, orphanGame);
    var panel = window.FindControl<Border>("OrphanBundlePanel")
                ?? throw new Exception("OrphanBundlePanel missing");
    var cleanButton = window.FindControl<Button>("OrphanCleanButton")
                      ?? throw new Exception("OrphanCleanButton missing");
    var cleanResult = window.FindControl<TextBlock>("OrphanCleanResult")
                      ?? throw new Exception("OrphanCleanResult missing");
    var orphanText = window.FindControl<TextBlock>("OrphanBundleText")
                     ?? throw new Exception("OrphanBundleText missing");

    // Bundles + state file = a live patch, not leftovers: nothing to offer here, 还原 owns it.
    refreshStatus.Invoke(window, null);
    Dispatcher.UIThread.RunJobs();
    if (panel.IsVisible)
        throw new Exception("the leftover panel must stay hidden while a state file explains the bundles");

    // Now the dead end as it actually happens: state.json gone, the client still reading our bundle.
    File.Delete(Path.Combine(home, "state.json"));
    refreshStatus.Invoke(window, null);
    Dispatcher.UIThread.RunJobs();
    if (!panel.IsVisible)
        throw new Exception("the leftover panel must appear when bundles exist with no state file");
    if (orphanText.Text?.Contains("1") != true)
        throw new Exception($"the panel should name how many files it found: {orphanText.Text}");

    // The cleanup scans the whole index on a background task. "Settled" = the button is live again
    // AND the result line says something new: SetBusy disables the button for the duration (a second
    // click would start a second scan), and waiting on the text is what proves the handler finished
    // rather than that the click merely landed.
    void PumpSettled(string before, string what)
    {
        for (var i = 0; i < 1200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
            if (cleanButton.IsEnabled && !string.Equals(cleanResult.Text, before, StringComparison.Ordinal))
                return;
        }

        throw new Exception($"the leftover cleanup never reported a result ({what}): {cleanResult.Text}");
    }

    var liveBeforeCleanup = File.ReadAllBytes(orphanIndex);
    var resultBeforeCleanup = cleanResult.Text ?? "";
    cleanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    PumpSettled(resultBeforeCleanup, "leftover cleanup");

    // The whole point: these bundles ARE referenced, so the click must refuse to delete them and
    // say so, instead of turning every suppressed file into a dangling record like --force would.
    if (!Directory.Exists(Path.Combine(orphanBundles, "LibGGPK3")))
        throw new Exception($"referenced bundles must survive the cleanup, result was: {cleanResult.Text}");
    if (cleanResult.Text?.Contains("引用") != true && cleanResult.Text?.Contains("referenced") != true)
        throw new Exception($"the result has to say what it kept: {cleanResult.Text}");
    if (File.ReadAllBytes(orphanIndex).SequenceEqual(liveBeforeCleanup) != true)
        throw new Exception("the cleanup must not touch the index");
    using (var read = PsCore.IndexSession.OpenReadOnly(orphanIndex, orphanBundles))
    {
        read.TryGetFile(orphanAoPath, out var strippedRecord);
        var text = Encoding.UTF8.GetString(strippedRecord!.Read().ToArray());
        if (text.Contains("AddAttached"))
            throw new Exception("the suppression has to keep working after a refused cleanup");
    }

    // A genuine leftover (client replaced underneath us): nothing references it, so it goes.
    orphanService.Restore(new PsCore.RestoreRequest
    {
        Game = orphanGame,
        BackupPath = orphanApplied.BackupPath,
        Force = true
    });
    var leftoverDir = Path.Combine(orphanBundles, "LibGGPK3");
    Directory.CreateDirectory(leftoverDir);
    File.WriteAllText(Path.Combine(leftoverDir, "0.bundle.bin"), "leftover from an older client");
    var cleanIndexBytes = File.ReadAllBytes(orphanIndex);
    refreshStatus.Invoke(window, null);
    Dispatcher.UIThread.RunJobs();
    if (!panel.IsVisible)
        throw new Exception("an unreferenced leftover with no state file must also raise the panel");
    var resultBeforeSecond = cleanResult.Text ?? "";
    cleanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    PumpSettled(resultBeforeSecond, "leftover cleanup 2");
    if (Directory.Exists(leftoverDir))
        throw new Exception($"an unreferenced leftover must be removed: {cleanResult.Text}");
    if (panel.IsVisible)
        throw new Exception("the panel must disappear once there is nothing left to clean");
    if (File.ReadAllBytes(orphanIndex).SequenceEqual(cleanIndexBytes) != true)
        throw new Exception("discarding a leftover must not rewrite the index");

    // The apply guard has to be off this case, which was the original complaint.
    gameField.SetValue(window, (PsCore.GameLocation?)null);
}

window.Show();
Dispatcher.UIThread.RunJobs();
Console.WriteLine("GUI headless smoke test passed.");
