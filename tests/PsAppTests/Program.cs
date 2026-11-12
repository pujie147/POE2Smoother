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
presetBox.SelectedItem = "performance";
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
        }
    ],
    Presets = new() { ["full"] = ["*"] }
}, foreignAll);
loadPack.Invoke(window, [foreignAll]);
refreshStatus.Invoke(window, null);
Dispatcher.UIThread.RunJobs();
if (!statusText.Text!.Contains("不在规则包") || !statusText.Text.Contains("not found"))
    throw new Exception($"unknown preset name should warn: {statusText.Text}");
if (!selectionText.Text!.Contains("已选 1/1"))
    throw new Exception($"unknown preset should fall back to all-selected: {selectionText.Text}");
if (!applyButton.IsEnabled)
    throw new Exception("apply should stay enabled while rows are selected");

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
presetBox.SelectedItem = "safe";
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
    var loopPetBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
    var loopMatBytes = new byte[] { 8, 9, 10, 11 };
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
    var loopExperimental = loopWindow.FindControl<CheckBox>("ExperimentalBox")
                           ?? throw new Exception("ExperimentalBox missing");
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

    if (loopExperimental.IsChecked != false)
        throw new Exception("experimental must start unticked");
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

    // 未勾选: only the .ao is suppressed, and the two blanking rules have to be reported
    // (in the status buckets and as a log hint pointing at the checkbox).
    ClickTwice(loopApply);
    PumpUntil(ApplySettled, "unticked apply");
    if (loopStatus.Text!.Contains("应用完成") != true)
        throw new Exception($"unticked apply failed: {loopStatus.Text}");
    if (!loopStatus.Text.Contains("1/3 active") || !loopStatus.Text.Contains("实验 2 / experimental"))
        throw new Exception($"skip bucket not reported while unticked: {loopStatus.Text}");
    if (loopLog.Text!.Contains("勾选“实验性资源”") != true)
        throw new Exception($"loading a pack with blanking rules should hint at the box: {loopLog.Text}");
    if (LiveBytes(loopPetPath).SequenceEqual(loopPetBytes) != true)
        throw new Exception(".pet must stay untouched while the box is unticked");
    if (LiveBytes(loopMatPath).SequenceEqual(loopMatBytes) != true)
        throw new Exception(".mat must stay untouched while the box is unticked");

    // 勾选实验性: the same click now has to reach the blanking rules too — the second apply
    // resets to the backup first, so the .ao is re-stripped instead of reporting "0 files".
    loopExperimental.IsChecked = true;
    ClickTwice(loopApply);
    PumpUntil(ApplySettled, "ticked apply");
    if (loopStatus.Text!.Contains("应用完成") != true)
        throw new Exception($"ticked apply failed: {loopStatus.Text}");
    if (!loopStatus.Text.Contains("3/3 active") || !loopStatus.Text.Contains("实验 0 / experimental"))
        throw new Exception($"ticking should move both skipped rules into the change set: {loopStatus.Text}");
    if (LiveBytes(loopPetPath).Length != 0)
        throw new Exception("ticked apply must blank the .pet");
    if (LiveBytes(loopMatPath).Length != 0)
        throw new Exception("ticked apply must blank the .mat");
    if (Encoding.UTF8.GetString(LiveBytes(loopAoPath)).Contains("AddAttached"))
        throw new Exception("ticked apply must keep suppressing the .ao");
    if (!File.Exists(loopStateFile))
        throw new Exception("apply must leave a state file behind");

    // restore: everything the tick wrote has to go back, byte for byte.
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

    // 勾选实验性 is a preference: the tick has to survive a restart, and an untick has to persist too.
    var saveSettings = typeof(MainWindow).GetMethod("SaveSettings",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("SaveSettings not found");
    saveSettings.Invoke(loopWindow, null);
    var savedSettings = File.ReadAllText(Path.Combine(home, "settings.json"));
    if (!savedSettings.Contains("\"IncludeExperimental\": true"))
        throw new Exception($"the tick must be persisted: {savedSettings}");
    var reopened = new MainWindow();
    if (reopened.FindControl<CheckBox>("ExperimentalBox")?.IsChecked != true)
        throw new Exception("a reopened window must restore the experimental tick");
    if (File.ReadAllText(Path.Combine(home, "settings.json")) != savedSettings)
        throw new Exception("opening a window must not rewrite the saved settings");

    // A profile carries the tick too: 存档案 → 取消勾选 → 载入档案 must put it back on.
    var loopProfileName = loopWindow.FindControl<TextBox>("ProfileNameBox")
                          ?? throw new Exception("ProfileNameBox missing");
    var saveProfile = typeof(MainWindow).GetMethod("OnSaveProfile",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("OnSaveProfile not found");
    var loadProfile = typeof(MainWindow).GetMethod("OnLoadProfile",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("OnLoadProfile not found");

    loopExperimental.IsChecked = true;
    loopProfileName.Text = "ui-experimental";
    saveProfile.Invoke(loopWindow, new object?[] { null, null });
    loopExperimental.IsChecked = false;
    loadProfile.Invoke(loopWindow, new object?[] { null, null });
    Dispatcher.UIThread.RunJobs();
    if (loopExperimental.IsChecked != true)
        throw new Exception("loading a profile must restore the experimental tick");

    loopExperimental.IsChecked = false;
    saveSettings.Invoke(loopWindow, null);
    if ((new MainWindow()).FindControl<CheckBox>("ExperimentalBox")?.IsChecked == true)
        throw new Exception("a reopened window must restore an unticked experimental box");

    // Leaving the box off after the loop means a later apply still never touches those files.
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
}

window.Show();
Dispatcher.UIThread.RunJobs();
Console.WriteLine("GUI headless smoke test passed.");
