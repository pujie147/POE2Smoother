using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

window.Show();
Dispatcher.UIThread.RunJobs();
Console.WriteLine("GUI headless smoke test passed.");
