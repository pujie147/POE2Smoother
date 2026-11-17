// Render the real window to PNG so a layout change can be *looked at*, not just asserted about:
// two rounds of green tests still hid a dead gap in every row and a duplicated English name.
//
//   dotnet run -c Release --project tools/gui-shot -- --pack <pack.json> --out /tmp/shots
//
// Needs Avalonia.Headless + Avalonia.Skia: `Avalonia.Headless` alone registers no rendering
// subsystem and SetupWithoutStarting() throws "No rendering system configured".
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PsApp;
using PsRules;

var argsList = args.ToList();
string Value(string flag, string fallback)
{
    var i = argsList.IndexOf(flag);
    return i >= 0 && i + 1 < argsList.Count ? argsList[i + 1] : fallback;
}

var pack = Value("--pack", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".local/share/poe2-smoother/rulepacks/default.json"));
var outDir = Value("--out", "/tmp/pszh/shots");
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(outDir);

// Never write into the player's real settings while taking a screenshot.
Environment.SetEnvironmentVariable("POE2SMOOTHER_HOME", Path.Combine(outDir, "home"));
Directory.CreateDirectory(Path.Combine(outDir, "home"));

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

var window = new MainWindow();
window.Show();
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();

var list = window.FindControl<ItemsControl>("CategoryGroupList")!;
var groundList = window.FindControl<ItemsControl>("GroundBranchList")!;
var searchBox = window.FindControl<TextBox>("CategorySearchBox")!;
var groupFilter = window.FindControl<ComboBox>("GroupFilterBox")!;
var languageBox = window.FindControl<ComboBox>("LanguageBox")!;
var tabs = window.GetLogicalDescendants().OfType<TabControl>().First();
var loadPack = typeof(MainWindow).GetMethod("LoadPack", BindingFlags.Instance | BindingFlags.NonPublic)!;

tabs.SelectedIndex = 1;                       // 特效 / Effects
Dispatcher.UIThread.RunJobs();
loadPack.Invoke(window, new object[] { pack });
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();

void Shoot(string name, Action setup)
{
    setup();
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    // Both trees, so the ignore tab's ground folders are visible too and not just its branches.
    foreach (var expander in list.GetLogicalDescendants().OfType<Expander>()
                 .Concat(groundList.GetLogicalDescendants().OfType<Expander>()))
        expander.IsExpanded = true;
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();

    var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
    using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
    rtb.Render(window);
    using var fs = File.Create(Path.Combine(outDir, name + ".png"));
    rtb.Save(fs);
    Console.WriteLine($"{name}: {size.Width}x{size.Height} rows={list.GetLogicalDescendants().OfType<CheckBox>().Count()}");
}

static object ItemOf(object? source, string id) =>
    ((System.Collections.IEnumerable)source!).Cast<object>().First(i => ((dynamic)i).Id == id);

void SelectLanguage(string id) => languageBox.SelectedItem = ItemOf(languageBox.ItemsSource, id);
void SelectGroup(string id) => groupFilter.SelectedItem = ItemOf(groupFilter.ItemsSource, id);

Shoot("skills-zh", () =>
{
    tabs.SelectedIndex = 1; SelectLanguage("zh"); searchBox.Text = ""; SelectGroup("skills");
});
Shoot("skills-mixed-zh", () =>
{
    tabs.SelectedIndex = 1; SelectLanguage("zh"); SelectGroup("skills"); searchBox.Text = "crossbow";
});
Shoot("env-zh", () =>
{
    tabs.SelectedIndex = 1; SelectLanguage("zh"); searchBox.Text = ""; SelectGroup("environment");
});
Shoot("mtx-en", () =>
{
    tabs.SelectedIndex = 1; SelectLanguage("en"); searchBox.Text = ""; SelectGroup("mtx");
});
Shoot("home-zh", () => tabs.SelectedIndex = 0);
// 遗留 bundle 面板平时藏着(只在"bundle 在、状态不在"时出现),所以强制显示它来验版式:
// 那段中英双语说明最长,能不能把主页挤变形只能看图,不能猜。
Shoot("home-orphan-zh", () =>
{
    tabs.SelectedIndex = 0;
    var panel = window.FindControl<Border>("OrphanBundlePanel")!;
    panel.IsVisible = true;
    window.FindControl<TextBlock>("OrphanBundleText")!.Text =
        "发现 1 个遗留自定义 bundle,但没有状态文件(0.bundle.bin) / 1 leftover custom bundle file(s), no state file";
    window.FindControl<TextBlock>("OrphanCleanResult")!.Text =
        "删了 0 个,但仍有 1 个被 index 引用,说明客户端里还有生效的补丁 / removed 0, but 1 is still referenced";
});
// 忽略列表页:左边那棵地面特效树 + 右边那份列表,同一屏要同时成立。
Shoot("ignore-zh", () => tabs.SelectedIndex = 2);
// 勾上一整个类别之后的样子:子目录必须是"勾上且灰掉",顶层必须是半选 —— 这三种状态在
// 同一屏里同时成立才算数,光看断言看不出色差与缩进。
Shoot("ignore-ticked-zh", () =>
{
    tabs.SelectedIndex = 2; SelectLanguage("zh");
    var set = typeof(MainWindow).GetMethod("SetGroundProtection",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    // 勾第一类就够:同一屏里三种状态同时出现 —— 顶层半选、类别行勾着且可点、
    // 它名下的子目录"勾上但灰掉"(取消它得动父行那句话才成立)。
    set.Invoke(window, new object[] { new List<string> { GroundEffectProtection.GroundEffectsV1 }, true });
});
