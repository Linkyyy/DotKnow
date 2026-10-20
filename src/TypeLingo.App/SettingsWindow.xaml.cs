using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TypeLingo.App.Windows;
using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.App;

public partial class SettingsWindow : Window
{
    private sealed record Choice(string Code, string Label);
    private readonly SettingsService service;
    private readonly SessionStatistics statistics;
    private readonly Func<AppSettings, string, Task<IReadOnlyList<string>>> getModels;
    private readonly Func<AppSettings, string, Task<TranslationResult>> testTranslation;
    private readonly Func<Task> clearCache;
    private readonly Func<AppSettings, string, string?> save;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool loading = true;
    private int providerRevision;
    private readonly FavoriteStore favorites;
    private List<FavoriteEntry> reviewQueue = [];
    private int reviewIndex;

    public SettingsWindow(SettingsService service, SessionStatistics statistics,
        Func<AppSettings, string, string?> save,
        Func<AppSettings, string, Task<IReadOnlyList<string>>> getModels,
        Func<AppSettings, string, Task<TranslationResult>> testTranslation, Func<Task> clearCache, FavoriteStore? favorites = null, VocabularyStore? vocabulary = null, Action? vocabularyChanged = null)
    {
        this.service = service; this.statistics = statistics; this.save = save;
        this.getModels = getModels; this.testTranslation = testTranslation; this.clearCache = clearCache;
        this.favorites = favorites ?? new FavoriteStore(service.DirectoryPath, new DpapiProtector());
        this.vocabulary = vocabulary; this.vocabularyChanged = vocabularyChanged;
        InitializeComponent(); LoadSettings(); ShowMessage(service.LoadWarning ?? "");
        SourceInitialized += (_, _) =>
        {
            Native.ApplyWindowAppearance(new WindowInteropHelper(this).Handle, AppearanceTheme.IsDark(service.Current.Overlay.Theme, DesktopTheme.IsDark));
            // 首次显示时适配高 DPI 或较小的工作区，不用固定最小高度把保存区挤出屏幕。
            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(760, area.Width); MinHeight = Math.Min(520, area.Height);
            Width = Math.Min(1040, area.Width); Height = Math.Min(820, area.Height);
        };
        SizeChanged += (_, _) =>
        {
            SidebarColumn.Width = new GridLength(ActualWidth < 820 ? 180 : ActualWidth < 940 ? 196 : 216);
            PaletteGrid.Columns = ActualWidth < 940 ? 2 : 4;
        };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { LoadSettings(); timer.Start(); }
            else { timer.Stop(); KeyInput.Clear(); UiText.Apply(service.Current.UiLanguage); DesktopTheme.ApplyResources(service.Current.Overlay); }
        };
        timer.Tick += (_, _) => RefreshStatistics();
    }
    private string SelectedTheme() => DarkThemeRadio.IsChecked == true ? "dark" : LightThemeRadio.IsChecked == true ? "light" : "system";
    private RadioButton[] PaletteChoices => [PaperPaletteRadio, ForestPaletteRadio, OceanPaletteRadio, LavenderPaletteRadio];
    private string SelectedPalette() => PaletteChoices.FirstOrDefault(x => x.IsChecked == true)?.Tag as string ?? AppearancePalettes.DefaultId;
    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        ApplySelectedAppearance();
    }
    private void ApplySelectedAppearance()
    {
        var theme = service.Current.Overlay with { Theme = SelectedTheme(), ColorTheme = SelectedPalette() };
        DesktopTheme.ApplyResources(theme);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) Native.ApplyWindowAppearance(handle, AppearanceTheme.IsDark(theme.Theme, DesktopTheme.IsDark));
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Navigate_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string id } && Pages.ContainsKey(id)) OpenPage(id); }
    private void HomeStart_Click(object sender, RoutedEventArgs e) => OpenPage(service.Current.PrivacyConsent ? "general" : "privacy");
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => Hide();
    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(service.DirectoryPath, "settings.json");
            if (!System.IO.File.Exists(path)) service.Save(service.Current);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception) { ShowMessage("无法打开配置文件，请检查文件关联与本地存储权限。"); }
    }
    public void ShowMessage(string message) => MessageText.Text = UiText.Get(message);
    private Dictionary<string, FrameworkElement> Pages => new()
    {
        ["home"] = PageHome, ["general"] = PageGeneral, ["ai"] = PageAi,
        ["cache"] = PageCache, ["appearance"] = PageAppearance, ["hotkeys"] = PageHotkeys, ["privacy"] = PagePrivacy, ["favorites"] = PageFavorites, ["vocabulary"] = PageVocabulary
    };
    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageTitleText == null || NavigationList.SelectedItem is not ListBoxItem { Tag: string id }) return;
        ShowSection(id);
    }
    private void ShowSection(string id)
    {
        var section = SettingsNavigation.Sections.First(x => x.Id == id);
        foreach (var page in Pages) page.Value.Visibility = page.Key == id ? Visibility.Visible : Visibility.Collapsed;
        PageHeader.Visibility = id == "home" ? Visibility.Collapsed : Visibility.Visible;
        PageTitleText.Text = UiText.Get(section.Title); PageDescriptionText.Text = UiText.Get(section.Description);
        PageScroll.Visibility = Visibility.Visible; PageScroll.ScrollToTop();
        if (id == "favorites") RefreshFavorites();
        if (id == "vocabulary") _ = RefreshVocabularyAsync(true);
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    { if (!loading && NavigationList != null) ApplySearch(); }
    private void Search_FocusChanged(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => RefreshSearchPlaceholder();
    private void RefreshSearchPlaceholder()
    {
        if (SearchPlaceholder != null)
            SearchPlaceholder.Visibility = SettingsSearchInput.Text.Length == 0 && !SettingsSearchInput.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplySearch()
    {
        RefreshSearchPlaceholder();
        var matches = SettingsNavigation.Search(SettingsSearchInput.Text, UiText.Language);
        foreach (ListBoxItem item in NavigationList.Items) item.Visibility = matches.Contains((string)item.Tag) ? Visibility.Visible : Visibility.Collapsed;
        NoResultsText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (matches.Count == 0)
        {
            PageHeader.Visibility = Visibility.Visible;
            PageScroll.Visibility = Visibility.Collapsed;
            PageTitleText.Text = UiText.Get("搜索结果"); PageDescriptionText.Text = UiText.Get("未找到相关设置");
        }
        else if (NavigationList.SelectedItem is not ListBoxItem selected || !matches.Contains((string)selected.Tag))
            NavigationList.SelectedItem = NavigationList.Items.Cast<ListBoxItem>().First(x => matches.Contains((string)x.Tag));
        else ShowSection((string)selected.Tag);
    }
    private void OpenPage(string id)
    {
        SettingsSearchInput.Clear();
        NavigationList.SelectedItem = NavigationList.Items.Cast<ListBoxItem>().First(x => (string)x.Tag == id);
    }
    private void OpenGeneral_Click(object sender, RoutedEventArgs e) => OpenPage("general");
    private void OpenAi_Click(object sender, RoutedEventArgs e) => OpenPage("ai");
    private void OpenPrivacy_Click(object sender, RoutedEventArgs e) => OpenPage("privacy");
    private void Scope_Changed(object sender, RoutedEventArgs e) { if (!loading) RefreshScope(); }
    private void RefreshScope() => AllowedApplicationsCard.Visibility = AllApplicationsCheck.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).PauseMonitoring();
        EnabledCheck.IsChecked = false; RefreshStatistics(); ShowMessage("已暂停所有读取。");
    }
    private static string Selected(ComboBox combo) => combo.SelectedValue as string ?? "";
    private static string Number(double n) => n.ToString(CultureInfo.InvariantCulture);
    private static int Integer(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static double Decimal(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private Choice Option(string code, string label) => new(code, UiText.Get(label));
    private void PopulateChoices(string source, string target, string mode, string position, string provider, string trigger, bool showLoading)
    {
        TriggerInput.ItemsSource = new[] { Option("selection", "鼠标选中后翻译（默认）"), Option("hotkey", "按快捷键出现"), Option("typing", "输入后自动出现") };
        TriggerInput.SelectedValue = trigger;
        PopupDisplayInput.ItemsSource = new[] { Option("loading", "翻译中显示 Loading"), Option("result", "翻译完成后显示") };
        PopupDisplayInput.SelectedValue = showLoading ? "loading" : "result";
        UiLanguageInput.ItemsSource = new[] { new Choice("zh-CN", "简体中文"), new Choice("en-US", "English"), new Choice("ja-JP", "日本語") };
        UiLanguageInput.SelectedValue = UiText.Language;
        SourceInput.ItemsSource = new[] { Option("auto", "自动识别"), Option("zh", "中文"), Option("en", "英语"), Option("ja", "日语") };
        TargetInput.ItemsSource = new[] { Option("zh", "中文"), Option("en", "英语"), Option("ja", "日语") };
        SourceInput.SelectedValue = source; TargetInput.SelectedValue = target;
        ModeInput.ItemsSource = new[] { Option("natural", "自然翻译"), Option("literal", "直译"), Option("business", "商务翻译"), Option("keigo", "敬语翻译") };
        ModeInput.SelectedValue = mode;
        PositionInput.ItemsSource = new[] { Option("right", "右侧"), Option("below", "下方"), Option("left", "左侧"), Option("above", "上方") };
        PositionInput.SelectedValue = position;
        PresetInput.ItemsSource = TranslationPresets.All.Select(x => Option(x.Id, x.Label)).Append(Option("custom", "自定义方向")).ToArray();
        PresetInput.SelectedValue = TranslationPresets.Find(source, target);
        ProviderInput.ItemsSource = ProviderCatalog.All.Select(x => Option(x.Id, x.Label)).ToArray();
        ProviderInput.SelectedValue = provider;
    }
    private void LoadSettings()
    {
        loading = true;
        try
        {
            var s = service.Current; UiText.Apply(s.UiLanguage);
            PopulateChoices(s.Translation.SourceLanguage, s.Translation.TargetLanguage, s.Translation.Mode, s.Overlay.Position, s.AI.Provider, s.Overlay.TriggerMode, s.Overlay.ShowLoading);
            EnabledCheck.IsChecked = s.Translation.Enabled; ConsentCheck.IsChecked = s.PrivacyConsent;
            AllApplicationsCheck.IsChecked = s.MonitorAllApplications;
            DebounceInput.Text = s.Translation.DebounceMilliseconds.ToString(); IntervalInput.Text = s.Translation.MinimumRequestIntervalMilliseconds.ToString(); MaxInput.Text = s.Translation.MaxInputCharacters.ToString();
            BaseUrlInput.Text = s.AI.BaseUrl; ModelInput.ItemsSource = null; ModelInput.Text = s.AI.Model;
            TimeoutInput.Text = s.AI.TimeoutSeconds.ToString(); RetriesInput.Text = s.AI.MaxRetries.ToString();
            KeyInput.Clear(); RefreshKeyStatus(); PlatformAddress.Text = ProviderCatalog.Get(s.AI.Provider).PlatformUrl;
            MemoryCheck.IsChecked = s.Cache.MemoryEnabled; MemoryCapacityInput.Text = s.Cache.MemoryMaxEntries.ToString(); MemoryTtlInput.Text = s.Cache.MemoryTTLMinutes.ToString();
            DiskCheck.IsChecked = s.Cache.DiskEnabled; DiskCapacityInput.Text = s.Cache.DiskMaxEntries.ToString(); DiskTtlInput.Text = s.Cache.DiskTTLDays.ToString();
            PreferOfflineCheck.IsChecked = s.Vocabulary.PreferOffline; AutoLearnCheck.IsChecked = s.Vocabulary.AutoLearn; OnlineFallbackCheck.IsChecked = s.Vocabulary.AllowOnlineFallback;
            LightThemeRadio.IsChecked = s.Overlay.Theme == "light"; DarkThemeRadio.IsChecked = s.Overlay.Theme == "dark"; SystemThemeRadio.IsChecked = s.Overlay.Theme == "system";
            foreach (var choice in PaletteChoices) choice.IsChecked = (string)choice.Tag == s.Overlay.ColorTheme;
            AutoHideInput.Text = s.Overlay.AutoHideSeconds.ToString(); PreviewHotkey.Text = s.Hotkeys.Preview; FontInput.Text = Number(s.Overlay.FontSize); WidthInput.Text = Number(s.Overlay.MaxWidth); OpacityInput.Text = Number(s.Overlay.Opacity);
            ReplaceHotkey.Text = s.Hotkeys.Replace; ToggleHotkey.Text = s.Hotkeys.Toggle; CopyHotkey.Text = s.Hotkeys.Copy;
            AllowedInput.Text = string.Join(", ", s.AllowedApplications); ExcludedInput.Text = string.Join(", ", s.ExcludedApplications); RefreshStatistics();
            providerRevision++;
        }
        finally { loading = false; }
        ApplySelectedAppearance();
        RefreshScope();
        if (NavigationList.SelectedItem == null) OpenPage("home");
        ApplySearch();
    }
    private void UiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Selected(UiLanguageInput) is not { Length: > 0 } language) return;
        var source = Selected(SourceInput); var target = Selected(TargetInput); var mode = Selected(ModeInput); var position = Selected(PositionInput); var provider = Selected(ProviderInput); var trigger = Selected(TriggerInput); var showLoading = Selected(PopupDisplayInput) == "loading";
        loading = true;
        try
        {
            UiText.Apply(language); PopulateChoices(source, target, mode, position, provider, trigger, showLoading);
            RefreshKeyStatus(); RefreshStatistics(); ApplySearch(); MessageText.Text = TestStatus.Text = "";
        }
        finally { loading = false; }
    }
    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Selected(PresetInput) is not { Length: > 0 } id || id == "custom") return;
        var direction = TranslationPresets.Apply(id, new());
        loading = true;
        try { SourceInput.SelectedValue = direction.SourceLanguage; TargetInput.SelectedValue = direction.TargetLanguage; }
        finally { loading = false; }
    }
    private void Direction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        loading = true;
        try { PresetInput.SelectedValue = TranslationPresets.Find(Selected(SourceInput), Selected(TargetInput)); }
        finally { loading = false; }
    }
    private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Selected(ProviderInput) is not { Length: > 0 } provider) return;
        loading = true;
        try
        {
            var defaults = ProviderCatalog.Get(provider);
            // 切换服务商时清除尚未保存的密钥输入，避免把上一家的密钥误发给新平台。
            KeyInput.Clear(); BaseUrlInput.Text = defaults.BaseUrl; ModelInput.ItemsSource = null; ModelInput.Text = defaults.DefaultModel;
            PlatformAddress.Text = defaults.PlatformUrl; TestStatus.Text = ""; providerRevision++; RefreshKeyStatus();
        }
        finally { loading = false; }
    }
    private AiSettings SelectedAi() => service.Current.AI with { Provider = Selected(ProviderInput), BaseUrl = BaseUrlInput.Text.Trim().TrimEnd('/'), Model = ModelInput.Text.Trim() };
    private void RefreshKeyStatus() => KeyStatus.Text = UiText.Get(service.HasKeyFor(SelectedAi()) ? "密钥已加密保存，留空即可继续使用。" : "当前服务商和 API 地址尚未保存密钥。");
    private void BaseUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        KeyInput.Clear(); ModelInput.ItemsSource = null; providerRevision++; RefreshKeyStatus();
    }
    private void Platform_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ProviderCatalog.Get(Selected(ProviderInput)).PlatformUrl) { UseShellExecute = true }); }
        catch (Exception) { ShowMessage("无法打开浏览器，请复制下方官方平台地址。"); }
    }
    private AppSettings ReadSettings()
    {
        var candidate = new AppSettings
        {
            UiLanguage = Selected(UiLanguageInput), PrivacyConsent = ConsentCheck.IsChecked == true,
            MonitorAllApplications = AllApplicationsCheck.IsChecked == true,
            Translation = new() { Enabled = EnabledCheck.IsChecked == true, SourceLanguage = Selected(SourceInput), TargetLanguage = Selected(TargetInput), Mode = Selected(ModeInput), DebounceMilliseconds = Integer(DebounceInput.Text), MinimumRequestIntervalMilliseconds = Integer(IntervalInput.Text), MaxInputCharacters = Integer(MaxInput.Text) },
            AI = SelectedAi() with { TimeoutSeconds = Integer(TimeoutInput.Text), MaxRetries = Integer(RetriesInput.Text) },
            Cache = new() { MemoryEnabled = MemoryCheck.IsChecked == true, MemoryMaxEntries = Integer(MemoryCapacityInput.Text), MemoryTTLMinutes = Integer(MemoryTtlInput.Text), DiskEnabled = DiskCheck.IsChecked == true, DiskMaxEntries = Integer(DiskCapacityInput.Text), DiskTTLDays = Integer(DiskTtlInput.Text) },
            Vocabulary = new() { PreferOffline = PreferOfflineCheck.IsChecked == true, AutoLearn = AutoLearnCheck.IsChecked == true, AllowOnlineFallback = OnlineFallbackCheck.IsChecked == true },
            Overlay = new() { ShowLoading = Selected(PopupDisplayInput) == "loading", Theme = SelectedTheme(), ColorTheme = SelectedPalette(), DarkMode = SelectedTheme() == "dark", TriggerMode = Selected(TriggerInput), AutoHideSeconds = Integer(AutoHideInput.Text), FontSize = Decimal(FontInput.Text), MaxWidth = Decimal(WidthInput.Text), Opacity = Decimal(OpacityInput.Text), Position = Selected(PositionInput) },
            Hotkeys = new() { Replace = ReplaceHotkey.Text.Trim(), Toggle = ToggleHotkey.Text.Trim(), Copy = CopyHotkey.Text.Trim(), Preview = PreviewHotkey.Text.Trim() },
            AllowedApplications = Split(AllowedInput.Text), ExcludedApplications = Split(ExcludedInput.Text)
        };
        candidate.Validate();
        foreach (var text in new[] { candidate.Hotkeys.Replace, candidate.Hotkeys.Toggle, candidate.Hotkeys.Copy, candidate.Hotkeys.Preview })
            if (!HotkeyService.TryParse(text, out _, out _)) throw new ArgumentException("快捷键格式无效，例如 Ctrl+Alt+Enter。");
        return candidate;
    }
    private static string[] Split(string text) => text.Split([',', '，', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = ReadSettings(); var warning = save(config, KeyInput.Password); KeyInput.Clear(); RefreshKeyStatus();
            ShowMessage(warning ?? (!config.PrivacyConsent ? "已保存；未授权，所有文本读取保持暂停。" :
                config.MonitorAllApplications ? "已保存。监控全部可访问应用，排除列表仍生效。" : "已保存。仅监控允许列表中的应用。"));
            RefreshStatistics();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { ShowMessage(ex is ArgumentException ? ex.Message : "请输入有效数字（小数使用英文句点）。"); }
        catch (Exception) { ShowMessage("无法保存设置或加密密钥，请检查本地存储权限。"); }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false; var revision = providerRevision;
        try
        {
            var candidate = ReadSettings(); TestStatus.Text = UiText.Get("正在查询模型…");
            var models = await getModels(candidate, KeyInput.Password);
            // 异步模型列表不能覆盖用户刚切换的新服务商设置。
            if (revision != providerRevision) return;
            var selected = ModelInput.Text; ModelInput.ItemsSource = models; ModelInput.Text = selected;
            TestStatus.Text = UiText.Format("连接成功，已查询到 {0} 个模型。", models.Count) +
                (models.Contains(selected, StringComparer.Ordinal) ? "" : "\n" + UiText.Get("当前模型不在返回列表中，可以手动修改后测试翻译。"));
        }
        catch (Exception ex)
        {
            if (revision == providerRevision) TestStatus.Text = UiText.Get(ex is TranslationException or ArgumentException ? ex.Message : "模型查询失败；仍可手动填写模型 ID，请检查密钥、地址和账户权限。");
        }
        finally { TestButton.IsEnabled = true; }
    }
    private async void TranslateTest_Click(object sender, RoutedEventArgs e)
    {
        TranslateTestButton.IsEnabled = false; var revision = providerRevision;
        try
        {
            var candidate = ReadSettings(); TestStatus.Text = UiText.Get("正在测试固定短句 Hello…");
            var result = await testTranslation(candidate, KeyInput.Password);
            if (revision == providerRevision) TestStatus.Text = UiText.Format("固定短句测试成功：{0}", result.Text);
        }
        catch (Exception ex)
        {
            if (revision == providerRevision) TestStatus.Text = UiText.Get(ex is TranslationException or ArgumentException ? ex.Message : "测试翻译失败，请检查密钥、模型和网络。");
        }
        finally { TranslateTestButton.IsEnabled = true; }
    }
    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try { await clearCache(); ShowMessage("所有缓存已清除。"); }
        catch (Exception) { ShowMessage("缓存清理失败，请检查文件权限。"); }
    }
    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        try { service.DeleteKey(SelectedAi()); KeyInput.Clear(); RefreshKeyStatus(); ShowMessage("已删除当前服务商的密钥。"); }
        catch (Exception) { ShowMessage("无法删除密钥，请检查文件权限。"); }
    }
    private void FavoritesSearch_TextChanged(object sender, TextChangedEventArgs e)
    { if (!loading) RefreshFavorites(); }
    private void RefreshFavorites_Click(object sender, RoutedEventArgs e) => RefreshFavorites();
    private void RefreshFavorites()
    {
        try
        {
            var all = favorites.GetAll(); var due = favorites.GetDue().Count;
            var query = FavoritesSearchInput.Text.Trim();
            var matches = all.Where(x => x.Original.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Translation.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            FavoritesList.ItemsSource = matches;
            FavoritesEmptyText.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FavoritesEmptyPanel.Visibility = FavoritesEmptyText.Visibility;
            FavoritesCountText.Text = UiText.Format("共 {0} 条收藏 · {1} 条待复习", all.Count, due);
            StartReviewButton.IsEnabled = due > 0;
        }
        catch (Exception)
        {
            FavoritesList.ItemsSource = null; StartReviewButton.IsEnabled = false;
            ShowMessage("收藏文件无法读取，原文件已保留。请检查本地文件或 Windows 用户。");
        }
    }
    private void CopyFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FavoriteEntry entry }) return;
        try { System.Windows.Clipboard.SetText(entry.Translation); ShowMessage("译文已复制"); }
        catch (System.Runtime.InteropServices.COMException) { ShowMessage("剪贴板正忙，请重试。"); }
    }
    private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FavoriteEntry entry }) return;
        if (System.Windows.MessageBox.Show(this, UiText.Get("删除这条收藏及其复习进度？"), UiText.Get("删除收藏"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            favorites.Delete(entry.Id); reviewQueue = []; ReviewPanel.Visibility = Visibility.Collapsed; RefreshFavorites();
        }
        catch (Exception) { ShowMessage("收藏操作失败，请检查本地文件与存储权限。"); }
    }
    private void StartReview_Click(object sender, RoutedEventArgs e)
    {
        try { reviewQueue = favorites.GetDue().ToList(); reviewIndex = 0; ShowReviewCard(); }
        catch (Exception) { ShowMessage("收藏操作失败，请检查本地文件与存储权限。"); }
    }
    private void ShowReviewCard()
    {
        if (reviewIndex >= reviewQueue.Count)
        {
            ReviewPanel.Visibility = Visibility.Collapsed; RefreshFavorites(); ShowMessage("本轮复习完成。"); return;
        }
        var entry = reviewQueue[reviewIndex];
        ReviewPanel.Visibility = Visibility.Visible;
        ReviewOriginalText.Text = entry.Original; ReviewTranslationText.Text = entry.Translation;
        ReviewTranslationText.Visibility = ReviewRatingPanel.Visibility = Visibility.Collapsed;
        ShowAnswerButton.Visibility = Visibility.Visible;
        ReviewProgressText.Text = UiText.Format("复习 {0} / {1}", reviewIndex + 1, reviewQueue.Count);
        ReviewProgressBar.Maximum = Math.Max(1, reviewQueue.Count);
        ReviewProgressBar.Value = reviewIndex;
    }
    private void ShowAnswer_Click(object sender, RoutedEventArgs e)
    {
        ReviewTranslationText.Visibility = ReviewRatingPanel.Visibility = Visibility.Visible;
        ShowAnswerButton.Visibility = Visibility.Collapsed;
    }
    private void ReviewAgain_Click(object sender, RoutedEventArgs e) => RateReview(false);
    private void ReviewRemembered_Click(object sender, RoutedEventArgs e) => RateReview(true);
    private void RateReview(bool remembered)
    {
        if (reviewIndex >= reviewQueue.Count || ReviewRatingPanel.Visibility != Visibility.Visible) return;
        try { favorites.Review(reviewQueue[reviewIndex].Id, remembered); reviewIndex++; ShowReviewCard(); RefreshFavorites(); }
        catch (Exception) { ShowMessage("收藏操作失败，请检查本地文件与存储权限。"); }
    }
    private void RefreshStatistics()
    {
        var hits = statistics.CacheHits + statistics.VocabularyHits;
        var total = statistics.Successes + hits;
        HomeApiCount.Text = statistics.ApiCalls.ToString(CultureInfo.InvariantCulture);
        HomeCacheCount.Text = hits.ToString(CultureInfo.InvariantCulture);
        HomeSuccessCount.Text = total.ToString(CultureInfo.InvariantCulture);
        var current = service.Current;
        SidebarStateText.Text = UiText.Get(!current.PrivacyConsent ? "等待隐私授权" : current.Translation.Enabled ? "实时翻译已开启" : "实时翻译已暂停");
        HomeStartButton.Content = UiText.Get(current.PrivacyConsent ? "翻译偏好" : "开始使用");
        HomeStateText.Text = UiText.Get(!current.PrivacyConsent ? "等待隐私授权" : current.Translation.Enabled ? "实时翻译已开启" : "实时翻译已暂停") +
            " · " + UiText.Get(current.MonitorAllApplications ? "全部可访问应用" : "仅允许指定应用");
        StatisticsText.Text = UiText.Format("HTTP 请求 {0} · 题库命中 {1} · 缓存命中 {2}\n在线成功 {3} · 失败 {4} · 本地命中率 {5}%\n平均 API 延迟 {6} ms · 输入词元 {7} / 输出词元 {8}",
            statistics.ApiCalls, statistics.VocabularyHits, statistics.CacheHits, statistics.Successes, statistics.Failures, total == 0 ? 0 : hits * 100 / total,
            statistics.AverageLatencyMilliseconds.ToString("F0", CultureInfo.InvariantCulture), statistics.PromptTokens, statistics.CompletionTokens);
        if (PageVocabulary.Visibility == Visibility.Visible) _ = RefreshVocabularyAsync(false);
    }
}
