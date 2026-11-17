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
    foreach (var expander in list.GetLogicalDescendants().OfType<Expander>())
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
