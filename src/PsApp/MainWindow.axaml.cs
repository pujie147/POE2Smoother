using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private bool _materialConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = BuildInfo.Version;

        _service = new AppService(_paths);
        _profiles = new ProfileStore(_paths);
        _settings = SettingsStore.Load(_paths);
        CategoryGroupList.ItemsSource = _groups;

        // 恢复档位必须排在任何会触发 SaveSettings 的赋值之前,并且整个构造期间由 _loading 挡住
        // SelectionChanged:给 ComboBox 赋 ItemsSource/SelectedItem 会同步触发处理器,处理器里的
        // SaveSettings 读到还没恢复的档位,就会把用户保存的选择改写掉——85f908a 修的就是这个形状
        // (当时是复选框重启后静默丢失,下一次应用悄悄少屏蔽一批文件)。
        _loading = true;
        // Built from TierItem.For: the tier restored from settings has to compare equal to an item
        // in this list, so the labels must not be a second hand-written copy of the same text.
        TierBox.ItemsSource = new[]
        {
            TierItem.For(RiskTier.Safe),
            TierItem.For(RiskTier.Fx),
            TierItem.For(RiskTier.Material)
        };
        TierBox.SelectedItem = TierItem.For(_settings.EffectiveTier());
        BlankStyleBox.ItemsSource = new[]
        {
            WriteStrategyItem.For(WriteStrategy.Template),
            WriteStrategyItem.For(WriteStrategy.Empty),
            WriteStrategyItem.For(WriteStrategy.CompressionOnly),
            WriteStrategyItem.For(WriteStrategy.CompressThenTemplate),
            WriteStrategyItem.For(WriteStrategy.CompressThenEmpty)
        };
        BlankStyleBox.SelectedItem = WriteStrategyItem.For(_settings.EffectiveWriteStrategy());
        var compression = _settings.EffectiveCompression();
        CompressTexCheck.IsChecked = compression.Texture;
        TexDownscaleCheck.IsChecked = compression.TextureDownscale;
        CompressShaderCheck.IsChecked = compression.Shader;
        MeshLodCheck.IsChecked = compression.MeshLod;
        MeshDecimateCheck.IsChecked = compression.MeshDecimate;
        DecimateSlider.Value = compression.ClampedStrength;
        UpdateCompressionUi();
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

        RestoreScopeBox.ItemsSource = new[]
        {
            new RestoreScopeItem("全部还原(整档)/ Everything (whole index)", null),
            new RestoreScopeItem("只撤实验性(特效+材质)/ Experimental only",
                new HashSet<RiskTier> { RiskTier.Fx, RiskTier.Material }),
            new RestoreScopeItem("只撤材质 / Materials only",
                new HashSet<RiskTier> { RiskTier.Material }),
            new RestoreScopeItem("只撤脚本手术 / Script surgery only",
                new HashSet<RiskTier> { RiskTier.Safe })
        };
        RestoreScopeBox.SelectedIndex = 0;

        PresetBox.ItemsSource = new[] { "safe", "clean", "performance", "full" };
        LanguageBox.ItemsSource = new[] { "zh", "en" };
        LanguageBox.SelectedItem = _settings.Language;
        PresetBox.SelectedItem = _settings.LastPreset;

        DataDirBox.Text = _paths.Root;
        RulePackBox.Text = _paths.DefaultRulePackPath;
        LogPathBox.Text = AppLog.PathFor(_paths);
        _loading = false;

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
            var candidates = await Task.Run(GameLocator.FindCandidates);
            _game = candidates.FirstOrDefault(c => c.IsSteamBundles) ?? candidates.FirstOrDefault();

            if (_game == null)
            {
                GamePathBox.Text = "";
                KindText.Text = "";
                StatusText.Text = "未找到 PoE2 客户端（Steam 版 / 独立客户端）。 / No PoE2 client found (Steam or standalone).";
                AppendLog("No PoE2 client found. Use 浏览/Browse to pick Bundles2/_.index.bin or Content.ggpk, " +
                          "or set POE2_INDEX / POE2_GGPK.");
                return;
            }

            ShowDetectedGame();

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

    private void ShowDetectedGame()
    {
        if (_game == null)
            return;

        GamePathBox.Text = _game.TargetPath;
        _settings.LastTargetPath = _game.TargetPath;
        SaveSettings();
        UpdateKindText();
        AppendLog($"Detected [{_game.KindLabel}]: {_game.TargetPath}");
    }

    private static string GetKindLabel(GameLocation game)
        => game.IsGgpk ? "独立客户端 / standalone" : "Steam";

    private void UpdateKindText()
    {
        if (_game == null)
        {
            KindText.Text = "";
            return;
        }

        KindText.Text = _game.IsGgpk
            ? "类型 / Type: 独立客户端 Content.ggpk（实验性：写入不可原子回滚，过程中不要结束进程） / " +
              "standalone Content.ggpk (experimental: writes are not atomic — do not kill the process)"
            : "类型 / Type: Steam · Bundles2/_.index.bin";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Bundles2/_.index.bin 或 Content.ggpk / Pick Bundles2/_.index.bin or Content.ggpk",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PoE2 data file") { Patterns = ["*.bin", "*.ggpk"] },
                    FilePickerFileTypes.All
                ]
            });

            if (files.Count == 0)
                return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("无法解析所选路径 / could not resolve the selected path", warn: true);
                return;
            }

            UseManualTarget(path);
        }
        catch (Exception ex)
        {
            SetStatus($"选择失败 / Selection failed: {ex.Message}", warn: true);
            AppendLog(ex.ToString());
        }
    }

    private void UseManualTarget(string path)
    {
        GameLocation location;
        try
        {
            location = GameLocator.FromManualPath(path);
        }
        catch (Exception ex)
        {
            SetStatus($"路径不可用 / unsupported path: {ex.Message}", warn: true);
            return;
        }

        _game = location;
        _pack = null;
        ShowDetectedGame();
        SetStatus($"已选择 / selected: {location.KindLabel} · {location.TargetPath}");
        RefreshStatus();
    }

    private void OnElevate(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (Elevation.TryRelaunchElevated(out var error))
        {
            Close();
            return;
        }

        SetStatus($"无法提权 / cannot elevate: {error}", warn: true);
    }

    private static bool IsAccessDenied(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;
        }

        return false;
    }

    private void ShowElevationHint()
    {
        if (Elevation.CanRelaunch)
        {
            ElevateButton.IsVisible = true;
            SetStatus("写入游戏目录被拒绝：需要管理员权限，请点右上角“以管理员身份重启”。 / " +
                      "Access denied — administrator rights are needed. Use “Run as admin”.", warn: true);
        }
        else
        {
            SetStatus("写入游戏目录被拒绝：请以管理员身份重新启动本工具。 / " +
                      "Access denied — restart this tool as administrator.", warn: true);
        }

        AppendLog("Access denied while writing the game directory " +
                  "(a Steam/standalone install under Program Files needs elevation).");
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
            if (IsAccessDenied(ex))
            {
                ShowElevationHint();
            }
            else
            {
                StatusText.Text = $"生成规则包失败 / Build failed: {ex.Message}";
            }

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
            var (primary, secondary) = category.LabelPair(language);
            var row = new CategoryRow
            {
                Id = category.Id,
                Group = category.Group,
                Label = primary,
                LabelSecondary = secondary,
                EnglishCounts = language.Equals("en", StringComparison.OrdinalIgnoreCase),
                // Both languages, never just the displayed one: typing 火球 or stormblast should find
                // the same row regardless of what the language dropdown is set to.
                SearchText = string.Join(' ', new[]
                {
                    category.LabelFor("zh"), category.LabelFor("en"), category.Id, category.Group,
                    GroupLabel(category.Group)
                }).ToLowerInvariant(),
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

        UpdateTierHint();
    }

    /// <summary>
    /// Tells the user how much the current tier is holding back, split by tier — the old single
    /// "experimental" number hid the fact that .mat and .pet were behind the same tick.
    /// </summary>
    private void UpdateTierHint()
    {
        if (_pack == null)
            return;

        // CompressionOnly blanks nothing, so a "blanking rules skipped at this tier" hint would be
        // misleading — the tier still gates compression, but no blanking rule is being held back.
        if (CurrentWriteStrategy == WriteStrategy.CompressionOnly)
            return;

        var selectedCount = _rows.Count(r => r.IsSelected);
        if (selectedCount == 0)
            return;

        var ids = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = _pack.Categories.Where(c => ids.Contains(c.Id)).SelectMany(c => c.Rules).ToList();
        var fx = rules.Count(r => r.Tier == RiskTier.Fx);
        var material = rules.Count(r => r.Tier == RiskTier.Material);
        var hidden = rules.Count(r => r.Tier > CurrentTier);
        if (hidden == 0)
            return;

        var detail = (fx, material) switch
        {
            (> 0, > 0) => $"特效 {fx} 条、材质 {material} 条 / {fx} effect and {material} material rules",
            (> 0, _) => $"特效 {fx} 条 / {fx} effect rules",
            _ => $"材质 {material} 条 / {material} material rules"
        };
        AppendLog($"当前风险档「{RiskTiers.ToKey(CurrentTier)}」跳过 {hidden} 条置空规则({detail})——换档可纳入 / " +
                  $"{hidden} blanking rules skipped at tier “{RiskTiers.ToKey(CurrentTier)}” ({detail}) — raise the tier to include them.");
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

        return row.SearchText.Contains(query.ToLowerInvariant()) ||
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

        // Parsed now, persisted only once the write actually starts: the material panel and the
        // four-second arm both return early, and an abandoned apply must not leave edited ignore
        // lines on disk.
        _ignore = IgnoreList.FromLines((IgnoreBox.Text ?? "").Split('\n'));

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
            MaxTier = CurrentTier,
            WriteStrategy = CurrentWriteStrategy,
            Compression = CurrentCompression,
            CategoryFilter = selected
        };

        // Material blanking and render compression each get their own confirmation with real
        // numbers, replacing the four-second arm: they are the stricter gates, and clicking 应用
        // twice after an explicit panel would only train people to click through it.
        var compressionStrategy = WriteStrategies.IsCompressionContaining(CurrentWriteStrategy);
        if (!_materialConfirmed && (request.MaxTier == RiskTier.Material || compressionStrategy))
        {
            SetBusy(true, "计算影响… / Planning");
            PatchPlan preview;
            try
            {
                preview = await Task.Run(() => _service.BuildPlan(request));
            }
            finally
            {
                SetBusy(false, null);
            }

            // Material-tier compression (textures) is not blanking: count material blanking
            // separately so the panel never claims compressed files were emptied.
            var blanked = preview.Changes
                .Where(c => c.Tier == RiskTier.Material && !CompressionOptions.IsCompressionStrategy(c.Strategy))
                .ToList();

            if (blanked.Count > 0)
            {
                var blankedAs = CurrentBlankStyle == BlankStyle.Template
                    ? "替换成合法的最小空材质(原内容消失,但引用者能正常加载)"
                    : "替换成 0 字节文件(旧行为:引用它的东西会加载失败)";
                var blankedAsEn = CurrentBlankStyle == BlankStyle.Template
                    ? "with a minimal valid empty material (original content gone, referrers still load it)"
                    : "with a 0-byte file (old behaviour: whatever references it fails to load)";
                var blankedBytes = blanked.Sum(c => c.Size);
                var otherFiles = preview.Changes.Count - preview.MaterialFiles;
                var compressionZh = preview.CompressionFiles > 0
                    ? $"另有 {preview.CompressionFiles} 个文件做渲染压缩:保留视觉、降低负载,实验性,需实机验证。"
                    : "";
                var compressionEn = preview.CompressionFiles > 0
                    ? $" Plus {preview.CompressionFiles} render-compressed files: visuals kept, cheaper to render — experimental, verify in game."
                    : "";
                MaterialConfirmText.Text =
                    $"这个档位会把 {blanked.Count} 个 .mat 材质{blankedAs}" +
                    $"(丢弃 {Sizes.FormatBytes(blankedBytes)} 原始内容)," +
                    $"外加 {otherFiles} 个其它文件。{compressionZh} / " +
                    $"This tier blanks {blanked.Count} .mat materials {blankedAsEn} " +
                    $"({Sizes.FormatBytes(blankedBytes)} of original content discarded), " +
                    $"plus {otherFiles} other files.{compressionEn}";
                MaterialConfirmWarning.Text =
                    "材质被置空后,普通渲染(地面/角色/物体)可能一起变黑或缺失;工具会先备份,之后可以只撤销材质。 / " +
                    "Blanking materials can also black out ordinary rendering (ground, characters, objects). A backup is always written first, and materials can be restored on their own afterwards.";
                MaterialConfirmButton.Content = "确认置空材质 / Confirm material blanking";
                MaterialConfirmPanel.IsVisible = true;
                SetStatus("请在下方确认材质档变更。 / Confirm material-tier changes in the panel below.", warn: true);
                AppendLog("等待材质确认 / waiting for material confirmation");
                return;
            }

            if (compressionStrategy && (preview.CompressionFiles > 0 || preview.CompressionFallbackFiles > 0))
            {
                var fallbackZh = preview.CompressionFallbackFiles > 0
                    ? $"其中 {preview.CompressionFallbackFiles} 个无法压缩,回退置空。"
                    : "";
                var fallbackEn = preview.CompressionFallbackFiles > 0
                    ? $" {preview.CompressionFallbackFiles} of them cannot be compressed and fall back to blanking."
                    : "";
                MaterialConfirmText.Text =
                    $"将对 {preview.CompressionFiles} 个文件做渲染压缩:保留视觉、降低负载。{fallbackZh}" +
                    "实验性,需实机验证。 / " +
                    $"Render-compressing {preview.CompressionFiles} files: visuals kept, cheaper to render.{fallbackEn} " +
                    "Experimental — verify in game.";
                MaterialConfirmWarning.Text =
                    "渲染压缩是实验性的:压缩后的特效需实机验证;工具会先备份,之后可以还原。 / " +
                    "Render compression is experimental: verify the compressed effects in game. A backup is always written first, and you can restore afterwards.";
                MaterialConfirmButton.Content = "确认压缩 / Confirm compression";
                MaterialConfirmPanel.IsVisible = true;
                SetStatus("请在下方确认渲染压缩变更。 / Confirm render-compression changes in the panel below.", warn: true);
                AppendLog("等待压缩确认 / waiting for compression confirmation");
                return;
            }
        }

        if (!_applyArmed && !_materialConfirmed)
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
        _ignore.Save(_paths.DefaultIgnorePath);

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
                          $"实验 {plan.SkippedExperimental.Count} / experimental " +
                          $"(特效 {plan.SkippedFxCount} · 材质 {plan.SkippedMaterialCount})";

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
                        ? "所选类别全是置空类资源:把「风险档」调到 fx(特效)或 material(材质)后重试 / every matched rule blanks a resource — raise the risk tier to fx (effects) or material and retry"
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
            if (IsAccessDenied(ex))
            {
                ShowElevationHint();
            }
            else
            {
                StatusText.Text = $"应用失败 / Apply failed: {ex.Message}";
            }

            AppendLog(ex.ToString());
        }
        finally
        {
            // Every material-tier write needs its own confirmation: arming once must not cover the
            // next apply either, or the panel degrades into another click-through warning.
            _materialConfirmed = false;
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
            var scope = (RestoreScopeBox.SelectedItem as RestoreScopeItem)?.Tiers;
            var result = await Task.Run(() => _service.Restore(
                new RestoreRequest { Game = _game, UndoTiers = scope }));
            if (result.Scoped)
                AppendLog($"按档位还原:撤销 {result.FilesRestored} 个,保留 {result.FilesKept} 个仍生效的屏蔽 / " +
                          $"scoped restore: {result.FilesRestored} undone, {result.FilesKept} still suppressed.");
            AppendLog(result.Message);
            StatusText.Text = result.RestoredIndex || result.PerFile || result.CleanedCustomBundles
                ? "还原完成 / Restored."
                : result.Message;
        }
        catch (Exception ex)
        {
            if (IsAccessDenied(ex))
            {
                ShowElevationHint();
            }
            else
            {
                StatusText.Text = $"还原失败 / Restore failed: {ex.Message}";
            }

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
        var profile = new Profile
        {
            Name = name,
            Preset = PresetBox.SelectedItem as string ?? "safe",
            Categories = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToList(),
            Ignore = _ignore.Prefixes.ToList()
        };
        profile.SetTier(CurrentTier);
        profile.SetWriteStrategy(CurrentWriteStrategy);
        profile.Compression = CurrentCompression;
        _profiles.Save(profile);
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
        TierBox.SelectedItem = TierItem.For(profile.EffectiveTier());
        BlankStyleBox.SelectedItem = WriteStrategyItem.For(profile.EffectiveWriteStrategy());
        var compression = profile.EffectiveCompression();
        CompressTexCheck.IsChecked = compression.Texture;
        TexDownscaleCheck.IsChecked = compression.TextureDownscale;
        CompressShaderCheck.IsChecked = compression.Shader;
        MeshLodCheck.IsChecked = compression.MeshLod;
        MeshDecimateCheck.IsChecked = compression.MeshDecimate;
        DecimateSlider.Value = compression.ClampedStrength;
        UpdateCompressionUi();
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
            $"游戏 / Game: {GetKindLabel(status.Game)} · {status.Game.TargetPath}",
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

        // Per tier, because one "experimental" number made a 50k-rule .mat class look identical
        // to a 2.5k-rule .fxgraph class.
        var fx = _pack.Categories.Sum(c => c.Rules.Count(r => r.Tier == RiskTier.Fx));
        var material = _pack.Categories.Sum(c => c.Rules.Count(r => r.Tier == RiskTier.Material));
        return $"规则包 / Pack: {_pack.Name} · {_pack.RuleCount} rules · " +
               $"{_pack.Categories.Count} categories · 特效 {fx} · 材质 {material} / " +
               $"{fx} fx · {material} material";
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

    /// <summary>The tier the user is currently armed with. Never null-safe-by-accident: an
    /// uninitialised ComboBox (no selection yet) means the safest tier, not "include everything".</summary>
    private RiskTier CurrentTier => (TierBox.SelectedItem as TierItem)?.Tier ?? RiskTier.Safe;

    /// <summary>The write strategy the user is currently armed with. Anything unrecognised means
    /// template, never zero bytes or compression: a half-initialised combo box must not silently
    /// downgrade the client or start compressing.</summary>
    private WriteStrategy CurrentWriteStrategy =>
        (BlankStyleBox.SelectedItem as WriteStrategyItem)?.Strategy ?? WriteStrategy.Template;

    /// <summary>What a blanking strategy writes, derived from the current write strategy.</summary>
    private BlankStyle CurrentBlankStyle => WriteStrategies.ToBlankStyle(CurrentWriteStrategy);

    /// <summary>Compression is opt-in per class: an uninitialised check box means off, never on.</summary>
    private CompressionOptions CurrentCompression => new()
    {
        Texture = CompressTexCheck.IsChecked == true,
        TextureDownscale = TexDownscaleCheck.IsChecked == true,
        Shader = CompressShaderCheck.IsChecked == true,
        MeshLod = MeshLodCheck.IsChecked == true,
        MeshDecimate = MeshDecimateCheck.IsChecked == true,
        DecimateStrength = (int)DecimateSlider.Value
    };

    private void UpdateCompressionUi()
    {
        // The compression toggles only appear when the chosen write strategy actually compresses.
        CompressionPanel.IsVisible = WriteStrategies.IsCompressionContaining(CurrentWriteStrategy);
        TexDownscaleCheck.IsEnabled = CompressTexCheck.IsChecked == true;
        DecimateSlider.IsEnabled = MeshDecimateCheck.IsChecked == true;
        DecimateValueText.Text = ((int)DecimateSlider.Value).ToString();
    }

    private void OnCompressionChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _materialConfirmed = false;
        MaterialConfirmPanel.IsVisible = false;
        UpdateCompressionUi();
        SaveSettings();
    }

    private void OnDecimateStrengthChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        DecimateValueText.Text = ((int)e.NewValue).ToString();
        if (_loading)
            return;

        SaveSettings();
    }

    private void OnWriteStrategyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        // Changing the strategy invalidates any pending confirmation, exactly like changing the tier.
        _materialConfirmed = false;
        MaterialConfirmPanel.IsVisible = false;
        UpdateCompressionUi();
        SaveSettings();
        AppendLog($"写法策略:{WriteStrategies.ToKey(CurrentWriteStrategy)} / write strategy “{WriteStrategies.ToKey(CurrentWriteStrategy)}”");
    }

    private void OnTierChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selecting an item during construction must not persist a half-restored window
        // (that is exactly how the old checkbox lost its tick across restarts — see 85f908a).
        if (_loading)
            return;

        _materialConfirmed = false;
        MaterialConfirmPanel.IsVisible = false;
        SaveSettings();
        UpdateTierHint();
    }

    private void OnConfirmMaterialApply(object? sender, RoutedEventArgs e)
    {
        MaterialConfirmPanel.IsVisible = false;
        _materialConfirmed = true;
        // OnApply is an async void handler: confirming resumes it rather than awaiting it, exactly
        // as clicking the button would.
        OnApply(sender, e);
    }

    private void OnCancelMaterialApply(object? sender, RoutedEventArgs e)
    {
        // Cancelling is "not this time", not "change my saved preference": the tier box stays where
        // the user left it, and the next 应用 click has to pass this panel again.
        MaterialConfirmPanel.IsVisible = false;
        _materialConfirmed = false;
        SetStatus("已取消,未写入任何文件 / cancelled — nothing was written.", warn: true);
        AppendLog("材质确认被取消,未写入 / material confirmation cancelled, nothing written");
    }

    private void SaveSettings()
    {
        _settings.LastPreset = PresetBox.SelectedItem as string ?? _settings.LastPreset;
        _settings.Language = LanguageBox.SelectedItem as string ?? _settings.Language;
        _settings.SetTier(CurrentTier);
        _settings.SetWriteStrategy(CurrentWriteStrategy);
        _settings.Compression = CurrentCompression;
        if (_game != null)
            _settings.LastTargetPath = _game.TargetPath;
        SettingsStore.Save(_paths, _settings);
    }
}

public sealed class CategoryRow : INotifyPropertyChanged
{
    private bool _selected;

    public string Id { get; init; } = "";
    public string Label { get; init; } = "";

    /// <summary>The other language, or null when both sides would say the same thing.</summary>
    public string? LabelSecondary { get; init; }
    public bool HasSecondary => LabelSecondary != null;
    public string FullLabel => LabelSecondary == null ? Label : $"{Label} / {LabelSecondary}";

    /// <summary>Lower-cased zh+en+id+group, so a search hits either language whichever one is primary.</summary>
    public string SearchText { get; init; } = "";

    public string Group { get; init; } = "";
    public int Count { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Row language follows the 语言 setting like the label does; "files" alone looked out of place.</summary>
    public string CountText => EnglishCounts ? $"{Count} files" : $"{Count} 个文件";
    public bool EnglishCounts { get; init; }

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

/// <summary>One entry of the risk-tier combo box. Kept as an explicit item type so the box can
/// show bilingual text while the code only ever reads the enum.</summary>
public sealed record TierItem(RiskTier Tier, string Label)
{
    public override string ToString() => Label;

    public static TierItem For(RiskTier tier) => tier switch
    {
        RiskTier.Material => new TierItem(RiskTier.Material, "含材质置空(危险)/ + blank materials (.mat)"),
        RiskTier.Fx => new TierItem(RiskTier.Fx, "含特效资源置空 / + blank effects (.pet/.epk/.fxgraph)"),
        _ => new TierItem(RiskTier.Safe, "安全:只动脚本 / Safe: script surgery only")
    };
}

/// <summary>One entry of the restore scope box; null tiers means "everything", which keeps the
/// whole-index restore path (and its byte-identical result) available.</summary>
public sealed record RestoreScopeItem(string Label, IReadOnlySet<RiskTier>? Tiers)
{
    public override string ToString() => Label;
}

/// <summary>One entry of the write-strategy combo box (see TierItem for why the label is a record).</summary>
public sealed record WriteStrategyItem(WriteStrategy Strategy, string Label)
{
    public override string ToString() => Label;

    public static WriteStrategyItem For(WriteStrategy strategy) => strategy switch
    {
        WriteStrategy.Empty => new WriteStrategyItem(WriteStrategy.Empty, "0 字节(旧行为)/ 0-byte file"),
        WriteStrategy.CompressionOnly => new WriteStrategyItem(WriteStrategy.CompressionOnly, "只渲染压缩 / Render-compress only"),
        WriteStrategy.CompressThenTemplate => new WriteStrategyItem(WriteStrategy.CompressThenTemplate, "先压缩,无法压缩则合法空资源 / Compress, else valid empty"),
        WriteStrategy.CompressThenEmpty => new WriteStrategyItem(WriteStrategy.CompressThenEmpty, "先压缩,无法压缩则 0 字节 / Compress, else 0-byte"),
        _ => new WriteStrategyItem(WriteStrategy.Template, "合法空资源 / valid empty resource")
    };
}

public sealed record GroupFilterItem(string Id, string Label)
{
    public override string ToString() => Label;
}