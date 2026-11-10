using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PsCore;
using PsRules;

namespace PsApp;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths = new();
    private readonly AppService _service;
    private readonly ProfileStore _profiles;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<CategoryRow> _rows = [];
    private readonly ObservableCollection<CategoryGroupRow> _groups = [];

    private GameLocation? _game;
    private RulePack? _pack;
    private IgnoreList _ignore = IgnoreList.Default();
    private bool _loading;
    private bool _restoreArmed;
    private bool _applyArmed;

    public MainWindow()
    {
        InitializeComponent();

        _service = new AppService(_paths);
        _profiles = new ProfileStore(_paths);
        _settings = SettingsStore.Load(_paths);
        CategoryGroupList.ItemsSource = _groups;

        GroupFilterBox.ItemsSource = new[]
        {
            new GroupFilterItem("all", "全部 / All"),
            new GroupFilterItem("skills", "技能 / Skills"),
            new GroupFilterItem("mtx", "外观 / MTX"),
            new GroupFilterItem("monsters", "怪物 / Monsters"),
            new GroupFilterItem("leagues", "联赛 / Leagues"),
            new GroupFilterItem("environment", "环境 / Environment"),
            new GroupFilterItem("misc", "其他 / Misc")
        };
        GroupFilterBox.SelectedIndex = 0;

        PresetBox.ItemsSource = new[] { "safe", "clean", "performance", "full" };
        LanguageBox.ItemsSource = new[] { "zh", "en" };
        LanguageBox.SelectedItem = _settings.Language;
        PresetBox.SelectedItem = _settings.LastPreset;

        DataDirBox.Text = _paths.Root;
        RulePackBox.Text = _paths.DefaultRulePackPath;
        LogPathBox.Text = AppLog.PathFor(_paths);
        ExperimentalBox.IsChecked = _settings.IncludeExperimental;

        LogBox.Text = string.Join(Environment.NewLine, AppLog.ReadTail(_paths, 120));
        IgnoreBox.Text = string.Join(Environment.NewLine, _ignore.Prefixes);
        RefreshProfiles();

        Opened += OnOpened;
        Closing += (_, _) => SaveSettings();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await DetectAsync();
    }

    private async Task DetectAsync()
    {
        SetBusy(true, "检测中… / Detecting…");
        try
        {
            var candidates = await Task.Run(SteamLibraryLocator.FindCandidates);
            _game = candidates.FirstOrDefault(c => c.IsSteamBundles);

            if (_game == null)
            {
                GamePathBox.Text = "";
                StatusText.Text = "未找到 PoE2 Steam 客户端。 / PoE2 Steam client not found.";
                AppendLog("No PoE2 Steam client found. Check the Steam library or set POE2_INDEX.");
                return;
            }

            GamePathBox.Text = _game.IndexPath;
            _settings.LastIndexPath = _game.IndexPath;
            SaveSettings();
            AppendLog($"Detected: {_game.IndexPath}");

            if (File.Exists(_paths.DefaultRulePackPath))
                LoadPack(_paths.DefaultRulePackPath);
            else
                await BuildRulesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"检测失败 / Detect failed: {ex.Message}";
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private async Task BuildRulesAsync()
    {
        if (_game == null)
        {
            StatusText.Text = "请先检测游戏。 / Detect the game first.";
            return;
        }

        SetBusy(true, "生成规则包… / Building rule pack…");
        try
        {
            var (pack, stats) = await Task.Run(() =>
                _service.BuildRules(_game, "PoE2 Core Effects", "unknown"));

            RulePackStore.Save(pack, _paths.DefaultRulePackPath);
            _ignore = IgnoreList.FromLines(pack.Ignore);
            _ignore.Save(_paths.DefaultIgnorePath);
            IgnoreBox.Text = string.Join(Environment.NewLine, _ignore.Prefixes);

            _pack = pack;
            RefreshCategories();
            AppendLog($"Rule pack built: {stats.Rules} rules, {stats.Categories} categories, {stats.Skipped} skipped.");
            StatusText.Text = pack.RuleCount == 0
                ? "规则包为空：index 路径解析可能失败，请查看日志。/ Rule pack is empty: path parsing may have failed."
                : $"规则包已生成 / Rule pack built: {stats.Rules} rules.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"生成规则包失败 / Build failed: {ex.Message}";
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private void LoadPack(string path)
    {
        try
        {
            _pack = RulePackStore.Load(path);
            RefreshCategories();
            AppendLog($"Rule pack loaded: {path}");
        }
        catch (Exception ex)
        {
            _pack = null;
            AppendLog($"Failed to load rule pack: {ex.Message}");
        }
    }

    private void RefreshCategories()
    {
        _rows.Clear();

        if (_pack == null)
        {
            RefreshVisibleGroups();
            return;
        }

        var preset = PresetBox.SelectedItem as string ?? "safe";
        var presetKnown = _pack.Presets.ContainsKey(preset);
        var patterns = _pack.Presets.TryGetValue(preset, out var p) ? p : ["*"];
        var language = LanguageBox.SelectedItem as string ?? "zh";

        foreach (var category in _pack.Categories)
        {
            var row = new CategoryRow
            {
                Id = category.Id,
                Group = category.Group,
                Label = category.LabelFor(language),
                Count = category.Rules.Count,
                Paths = category.Rules.Select(r => r.Path).ToList(),
                IsSelected = patterns.Any(pattern => PresetSelector.Matches(category.Id, pattern))
            };
            row.PropertyChanged += (_, _) => UpdateSelectionText();
            _rows.Add(row);
        }

        RefreshVisibleGroups();

        var selectedCount = _rows.Count(r => r.IsSelected);
        if (!presetKnown)
        {
            SetStatus($"⚠ 预设 “{preset}” 不在规则包中,已按 * 全选 / preset “{preset}” not found in the rule pack — everything was selected.", warn: true);
        }
        else if (_rows.Count > 0 && selectedCount == 0)
        {
            SetStatus($"⚠ 预设 “{preset}” 未匹配任何类别(0/{_rows.Count}):换用 full、手动勾选或重新生成规则包 / preset “{preset}” matched 0 of {_rows.Count} categories — use “full”, tick rows, or rebuild rules.", warn: true);
        }
        else
        {
            SetStatus($"预设 {preset}:已选 {selectedCount}/{_rows.Count} 类 / {selectedCount}/{_rows.Count} categories selected.");
        }

        if (ExperimentalBox.IsChecked != true && selectedCount > 0)
        {
            var ids = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var experimental = _pack.Categories
                .Where(c => ids.Contains(c.Id))
                .Sum(c => c.Rules.Count(r => r.Experimental));
            if (experimental > 0)
                AppendLog($"另有 {experimental} 条材质/粒子规则(.mat/.pet/.epk 等)按“实验性”跳过——勾选“实验性资源”可纳入 / {experimental} experimental rules skipped unless “Experimental” is enabled.");
        }
    }

    private void RefreshVisibleGroups()
    {
        var query = (CategorySearchBox.Text ?? "").Trim();
        var groupId = (GroupFilterBox.SelectedItem as GroupFilterItem)?.Id ?? "all";

        _groups.Clear();

        var filtered = _rows
            .Where(row => groupId == "all" ||
                          row.Group.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            .Where(row => Matches(query, row))
            .ToList();

        foreach (var group in filtered
                     .GroupBy(row => row.Group, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => GroupOrder(group.Key)))
        {
            _groups.Add(new CategoryGroupRow
            {
                Group = group.Key,
                Header = $"{GroupLabel(group.Key)} ({group.Count()})",
                Categories = new ObservableCollection<CategoryRow>(group)
            });
        }

        FilterEmptyText.IsVisible = _groups.Count == 0;
        FilterEmptyText.Text = _pack == null
            ? "无规则包 / No rule pack"
            : _rows.Count == 0
                ? "规则包为空 / Rule pack is empty"
                : "没有匹配的类别 / No matching categories";
        UpdateSelectionText();
    }

    private static bool Matches(string query, CategoryRow row)
    {
        if (query.Length == 0)
            return true;

        return row.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Paths.Any(path => path.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateSelectionText()
    {
        if (_pack == null)
        {
            SelectionText.Text = "无规则包 / No rule pack";
            return;
        }

        var selected = _rows.Count(r => r.IsSelected);
        var visible = _groups.Sum(g => g.Categories.Count);
        foreach (var group in _groups)
            group.UpdateSelectionState();
        if (_rows.Count > 0 && selected == 0)
        {
            SelectionText.Foreground = Brush.Parse("#E5A64A");
            SelectionText.Text = $"已选 0/{_rows.Count} · 预设未匹配任何类别:勾选类别或切换预设 / preset matches nothing — tick rows or switch preset";
            return;
        }

        SelectionText.Foreground = Brush.Parse("#AAB2C0");
        SelectionText.Text = $"已选 {selected}/{_rows.Count} · 显示 {visible}/{_rows.Count} / selected";
    }

    private static int GroupOrder(string group) => group.ToLowerInvariant() switch
    {
        "skills" => 0,
        "mtx" => 1,
        "monsters" => 2,
        "leagues" => 3,
        "environment" => 4,
        "misc" => 5,
        _ => 9
    };

    private static string GroupLabel(string group) => group.ToLowerInvariant() switch
    {
        "skills" => "技能 / Skills",
        "mtx" => "外观 / MTX",
        "monsters" => "怪物 / Monsters",
        "leagues" => "联赛 / Leagues",
        "environment" => "环境 / Environment",
        "misc" => "其他 / Misc",
        _ => group
    };

    private async void OnBuildRules(object? sender, RoutedEventArgs e)
    {
        await BuildRulesAsync();
    }

    private async void OnDetect(object? sender, RoutedEventArgs e)
    {
        await DetectAsync();
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_game == null || _pack == null)
        {
            StatusText.Text = "请先检测游戏并生成规则包。 / Detect the game and build rules first.";
            return;
        }

        if (!_applyArmed)
        {
            _applyArmed = true;
            ApplyButton.Content = "再次点击应用 / Confirm apply";
            StatusText.Text = "再次点击“应用”以确认。 / Click Apply again to confirm.";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _applyArmed = false;
                ApplyButton.Content = "应用 / Apply";
            };
            timer.Start();
            return;
        }

        _applyArmed = false;
        ApplyButton.Content = "应用 / Apply";

        _ignore = IgnoreList.FromLines((IgnoreBox.Text ?? "").Split('\n'));
        _ignore.Save(_paths.DefaultIgnorePath);

        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            StatusText.Text = "没有选择任何类别。 / No categories selected.";
            return;
        }

        var request = new ApplyRequest
        {
            Game = _game,
            Pack = _pack,
            RulePackPath = _paths.DefaultRulePackPath,
            Preset = PresetBox.SelectedItem as string ?? "safe",
            Ignore = _ignore,
            IncludeExperimental = ExperimentalBox.IsChecked == true,
            CategoryFilter = selected
        };

        SetBusy(true, "应用中… / Applying…");
        try
        {
            var progress = new Progress<PatchProgress>(p =>
            {
                StatusText.Text = $"应用 {p.Done}/{p.Total} · {p.Phase} / Applying";
            });

            var result = await Task.Run(() => _service.Apply(request, progress));
            var plan = result.Plan;
            var attempted = plan.Changes.Count + plan.Unchanged.Count + plan.Ignored.Count
                + plan.Missing.Count + plan.Unsupported.Count + plan.SkippedExperimental.Count;
            var buckets = $"未变 {plan.Unchanged.Count} / unchanged · 忽略 {plan.Ignored.Count} / ignored · " +
                          $"缺失 {plan.Missing.Count} / missing · 不支持 {plan.Unsupported.Count} / unsupported · " +
                          $"实验 {plan.SkippedExperimental.Count} / experimental";

            if (plan.Changes.Count > 0)
            {
                AppendLog($"Applied {plan.Changes.Count} files (unchanged={plan.Unchanged.Count}, skippedExp={plan.SkippedExperimental.Count}). backup={result.BackupPath}");
                SetStatus($"应用完成 / applied — {plan.Changes.Count}/{attempted} active · {buckets}. 重启 PoE2 / Restart PoE2.");
            }
            else
            {
                var reason = plan.Missing.Count > 0 && plan.Missing.Count * 2 >= attempted
                    ? "规则包与当前 index 不匹配(游戏已更新?):先“还原”,再“生成规则包” / the rule pack is stale vs the current index — restore, then rebuild rules"
                    : plan.SkippedExperimental.Count == attempted
                        ? "所选类别全部是实验性资源:勾选“实验性资源”后重试 / every matched rule is experimental — enable “Experimental” and retry"
                        : plan.Ignored.Count == attempted
                            ? "全部被忽略列表拦截 / everything was blocked by the ignore list"
                            : plan.Unchanged.Count == attempted
                                ? "命中的文件与替换内容完全相同,无字节变化 / every matched file already contains the replacement — nothing to change"
                                : "勾选的类别没有命中任何文件 / the selection matched no files";
                AppendLog($"Apply produced 0 changes. reason: {reason}");
                SetStatus($"⚠ 没有生效:{reason}。未写入任何文件,游戏不会变化 / nothing applied, nothing written.", warn: true);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"应用失败 / Apply failed: {ex.Message}";
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (_game == null)
        {
            StatusText.Text = "请先检测游戏。 / Detect the game first.";
            return;
        }

        if (!_restoreArmed)
        {
            _restoreArmed = true;
            RestoreButton.Content = "再次点击确认 / Click again";
            StatusText.Text = "再次点击“还原”以确认。 / Click Restore again to confirm.";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _restoreArmed = false;
                RestoreButton.Content = "还原 / Restore";
            };
            timer.Start();
            return;
        }

        _restoreArmed = false;
        RestoreButton.Content = "还原 / Restore";
        SetBusy(true, "还原中… / Restoring…");
        try
        {
            var result = await Task.Run(() => _service.Restore(new RestoreRequest { Game = _game }));
            AppendLog(result.Message);
            StatusText.Text = result.RestoredIndex ? "还原完成 / Restored." : result.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"还原失败 / Restore failed: {ex.Message}";
            AppendLog(ex.ToString());
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        RefreshCategories();
        SaveSettings();
    }

    private void OnCategorySearchChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshVisibleGroups();
    }

    private void OnGroupFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshVisibleGroups();
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        CategorySearchBox.Text = "";
        GroupFilterBox.SelectedIndex = 0;
        RefreshVisibleGroups();
    }

    private void OnGroupCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: CategoryGroupRow group } check)
        {
            group.SetSelection(check.IsChecked == true);
            UpdateSelectionText();
        }
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = true;
    }

    private void OnClearAll(object? sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = false;
    }

    private void OnSaveIgnore(object? sender, RoutedEventArgs e)
    {
        _ignore = IgnoreList.FromLines((IgnoreBox.Text ?? "").Split('\n'));
        _ignore.Save(_paths.DefaultIgnorePath);
        StatusText.Text = "忽略列表已保存 / Ignore list saved.";
    }

    private void OnReloadIgnore(object? sender, RoutedEventArgs e)
    {
        _ignore = File.Exists(_paths.DefaultIgnorePath)
            ? IgnoreList.Load(_paths.DefaultIgnorePath)
            : IgnoreList.Default();
        IgnoreBox.Text = string.Join(Environment.NewLine, _ignore.Prefixes);
        StatusText.Text = "忽略列表已重新载入 / Ignore list reloaded.";
    }

    private void OnResetIgnore(object? sender, RoutedEventArgs e)
    {
        _ignore = IgnoreList.Default();
        IgnoreBox.Text = string.Join(Environment.NewLine, _ignore.Prefixes);
        StatusText.Text = "已恢复默认忽略列表 / Default ignore list restored.";
    }

    private void RefreshProfiles()
    {
        ProfileList.ItemsSource = _profiles.List();
    }

    private void OnSaveProfile(object? sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ProfileStatus.Text = "请输入档案名称。 / Enter a profile name.";
            return;
        }

        _ignore = IgnoreList.FromLines((IgnoreBox.Text ?? "").Split('\n'));
        _profiles.Save(new Profile
        {
            Name = name,
            Preset = PresetBox.SelectedItem as string ?? "safe",
            Categories = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToList(),
            Ignore = _ignore.Prefixes.ToList(),
            IncludeExperimental = ExperimentalBox.IsChecked == true
        });
        RefreshProfiles();
        ProfileStatus.Text = $"已保存 / Saved: {name}";
    }

    private void OnLoadProfile(object? sender, RoutedEventArgs e)
    {
        var name = (ProfileList.SelectedItem as string) ?? ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ProfileStatus.Text = "请选择档案。 / Select a profile.";
            return;
        }

        var profile = _profiles.Load(name);
        if (profile == null)
        {
            ProfileStatus.Text = $"档案不存在 / Profile not found: {name}";
            return;
        }

        _loading = true;
        PresetBox.SelectedItem = profile.Preset;
        ExperimentalBox.IsChecked = profile.IncludeExperimental;
        IgnoreBox.Text = string.Join(Environment.NewLine, profile.Ignore);
        RefreshCategories();

        var selected = profile.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
            row.IsSelected = selected.Contains(row.Id);

        _loading = false;
        ProfileNameBox.Text = profile.Name;
        ProfileStatus.Text = $"已载入 / Loaded: {profile.Name}";
    }

    private void OnDeleteProfile(object? sender, RoutedEventArgs e)
    {
        var name = (ProfileList.SelectedItem as string) ?? ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        _profiles.Delete(name);
        RefreshProfiles();
        ProfileStatus.Text = $"已删除 / Deleted: {name}";
    }

    private void OnProfileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is string name)
            ProfileNameBox.Text = name;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        _settings.Language = LanguageBox.SelectedItem as string ?? "zh";
        SaveSettings();
        RefreshCategories();
    }

    private void RefreshStatus()
    {
        if (_game == null)
        {
            StateText.Text = "未检测到游戏。 / No game detected.";
            PackText.Text = _pack == null ? "无规则包 / No rule pack" : PackSummary();
            ApplyButton.IsEnabled = false;
            RestoreButton.IsEnabled = false;
            return;
        }

        var status = _service.GetStatus(_game);
        var lines = new List<string>
        {
            $"游戏 / Game: {status.Game.GameRoot}",
            $"状态 / State: {(status.IsModified ? "已应用 / modified" : "原始 / original")}",
            $"游戏更新 / Game updated: {(status.GameUpdated ? "是 / yes" : "否 / no")}",
            $"自定义 bundle / Custom bundles: {(status.CustomBundlesExist ? "存在 / present" : "无 / none")}",
            $"备份 / Backup: {(status.State?.BackupPath ?? "无 / none")}"
        };

        if (status.GameUpdated)
            lines.Add("警告：游戏已更新，旧备份不匹配。请先还原/清理，再重新生成规则包。 / " +
                      "Warning: game updated. Restore/clean, then rebuild rules.");

        if (status.State != null)
            lines.Add($"已应用 / Applied: {status.State.FileCount} files · {status.State.Preset} · {status.State.AppliedAt:yyyy-MM-dd HH:mm}");

        StateText.Text = string.Join(Environment.NewLine, lines);
        PackText.Text = PackSummary();
        ApplyButton.IsEnabled = _pack != null && _rows.Any(r => r.IsSelected);
        RestoreButton.IsEnabled = status.State != null || status.CustomBundlesExist;
    }

    private string PackSummary()
    {
        if (_pack == null)
            return "无规则包 / No rule pack";

        var experimental = _pack.Categories.Sum(c => c.Rules.Count(r => r.Experimental));
        return $"规则包 / Pack: {_pack.Name} · {_pack.RuleCount} rules · " +
               $"{_pack.Categories.Count} categories · {experimental} experimental";
    }

    private void SetStatus(string text, bool warn = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = Brush.Parse(warn ? "#E5A64A" : "#AAB2C0");
    }

    private void SetBusy(bool busy, string? message)
    {
        DetectButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        BuildPackButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _game != null && _pack != null && _rows.Any(r => r.IsSelected);
        RestoreButton.IsEnabled = !busy && _game != null;
        if (message != null)
            StatusText.Text = message;
    }

    private void AppendLog(string message)
    {
        AppLog.Info(_paths, message);
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        LogBox.Text = string.IsNullOrEmpty(LogBox.Text) ? line : LogBox.Text + Environment.NewLine + line;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void SaveSettings()
    {
        _settings.LastPreset = PresetBox.SelectedItem as string ?? _settings.LastPreset;
        _settings.Language = LanguageBox.SelectedItem as string ?? _settings.Language;
        _settings.IncludeExperimental = ExperimentalBox.IsChecked == true;
        if (_game != null)
            _settings.LastIndexPath = _game.IndexPath;
        SettingsStore.Save(_paths, _settings);
    }
}

public sealed class CategoryRow : INotifyPropertyChanged
{
    private bool _selected;

    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public int Count { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
    public string CountText => $"{Count} files";

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CategoryGroupRow : INotifyPropertyChanged
{
    private bool? _isSelected;

    public string Group { get; init; } = "";
    public string Header { get; init; } = "";
    public ObservableCollection<CategoryRow> Categories { get; init; } = [];

    public bool? IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public void SetSelection(bool selected)
    {
        foreach (var category in Categories)
            category.IsSelected = selected;
        UpdateSelectionState();
    }

    public void UpdateSelectionState()
    {
        if (Categories.Count == 0)
        {
            IsSelected = false;
            return;
        }

        var selected = Categories.Count(c => c.IsSelected);
        IsSelected = selected == 0
            ? false
            : selected == Categories.Count
                ? true
                : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record GroupFilterItem(string Id, string Label)
{
    public override string ToString() => Label;
}