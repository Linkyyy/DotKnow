using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    public SettingsWindow(SettingsService service, SessionStatistics statistics,
        Func<AppSettings, string, string?> save,
        Func<AppSettings, string, Task<IReadOnlyList<string>>> getModels,
        Func<AppSettings, string, Task<TranslationResult>> testTranslation, Func<Task> clearCache)
    {
        this.service = service; this.statistics = statistics; this.save = save;
        this.getModels = getModels; this.testTranslation = testTranslation; this.clearCache = clearCache;
        InitializeComponent(); LoadSettings(); ShowMessage(service.LoadWarning ?? "");
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { LoadSettings(); timer.Start(); }
            else { timer.Stop(); KeyInput.Clear(); UiText.Apply(service.Current.UiLanguage); }
        };
        timer.Tick += (_, _) => RefreshStatistics();
    }
    public void ShowMessage(string message) => MessageText.Text = UiText.Get(message);
    private static string Selected(ComboBox combo) => combo.SelectedValue as string ?? "";
    private static string Number(double n) => n.ToString(CultureInfo.InvariantCulture);
    private static int Integer(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static double Decimal(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private Choice Option(string code, string label) => new(code, UiText.Get(label));
    private void PopulateChoices(string source, string target, string mode, string position, string provider)
    {
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
            PopulateChoices(s.Translation.SourceLanguage, s.Translation.TargetLanguage, s.Translation.Mode, s.Overlay.Position, s.AI.Provider);
            EnabledCheck.IsChecked = s.Translation.Enabled; ConsentCheck.IsChecked = s.PrivacyConsent;
            DebounceInput.Text = s.Translation.DebounceMilliseconds.ToString(); IntervalInput.Text = s.Translation.MinimumRequestIntervalMilliseconds.ToString(); MaxInput.Text = s.Translation.MaxInputCharacters.ToString();
            BaseUrlInput.Text = s.AI.BaseUrl; ModelInput.ItemsSource = null; ModelInput.Text = s.AI.Model;
            TimeoutInput.Text = s.AI.TimeoutSeconds.ToString(); RetriesInput.Text = s.AI.MaxRetries.ToString();
            KeyInput.Clear(); RefreshKeyStatus(); PlatformAddress.Text = ProviderCatalog.Get(s.AI.Provider).PlatformUrl;
            MemoryCheck.IsChecked = s.Cache.MemoryEnabled; MemoryCapacityInput.Text = s.Cache.MemoryMaxEntries.ToString(); MemoryTtlInput.Text = s.Cache.MemoryTTLMinutes.ToString();
            DiskCheck.IsChecked = s.Cache.DiskEnabled; DiskCapacityInput.Text = s.Cache.DiskMaxEntries.ToString(); DiskTtlInput.Text = s.Cache.DiskTTLDays.ToString();
            DarkCheck.IsChecked = s.Overlay.DarkMode; FontInput.Text = Number(s.Overlay.FontSize); WidthInput.Text = Number(s.Overlay.MaxWidth); OpacityInput.Text = Number(s.Overlay.Opacity);
            ReplaceHotkey.Text = s.Hotkeys.Replace; ToggleHotkey.Text = s.Hotkeys.Toggle; CopyHotkey.Text = s.Hotkeys.Copy;
            AllowedInput.Text = string.Join(", ", s.AllowedApplications); ExcludedInput.Text = string.Join(", ", s.ExcludedApplications); RefreshStatistics();
            providerRevision++;
        }
        finally { loading = false; }
    }
    private void UiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Selected(UiLanguageInput) is not { Length: > 0 } language) return;
        var source = Selected(SourceInput); var target = Selected(TargetInput); var mode = Selected(ModeInput); var position = Selected(PositionInput); var provider = Selected(ProviderInput);
        loading = true;
        try
        {
            UiText.Apply(language); PopulateChoices(source, target, mode, position, provider);
            RefreshKeyStatus(); RefreshStatistics(); MessageText.Text = TestStatus.Text = "";
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
            Translation = new() { Enabled = EnabledCheck.IsChecked == true, SourceLanguage = Selected(SourceInput), TargetLanguage = Selected(TargetInput), Mode = Selected(ModeInput), DebounceMilliseconds = Integer(DebounceInput.Text), MinimumRequestIntervalMilliseconds = Integer(IntervalInput.Text), MaxInputCharacters = Integer(MaxInput.Text) },
            AI = SelectedAi() with { TimeoutSeconds = Integer(TimeoutInput.Text), MaxRetries = Integer(RetriesInput.Text) },
            Cache = new() { MemoryEnabled = MemoryCheck.IsChecked == true, MemoryMaxEntries = Integer(MemoryCapacityInput.Text), MemoryTTLMinutes = Integer(MemoryTtlInput.Text), DiskEnabled = DiskCheck.IsChecked == true, DiskMaxEntries = Integer(DiskCapacityInput.Text), DiskTTLDays = Integer(DiskTtlInput.Text) },
            Overlay = new() { DarkMode = DarkCheck.IsChecked == true, FontSize = Decimal(FontInput.Text), MaxWidth = Decimal(WidthInput.Text), Opacity = Decimal(OpacityInput.Text), Position = Selected(PositionInput) },
            Hotkeys = new() { Replace = ReplaceHotkey.Text.Trim(), Toggle = ToggleHotkey.Text.Trim(), Copy = CopyHotkey.Text.Trim() },
            AllowedApplications = Split(AllowedInput.Text), ExcludedApplications = Split(ExcludedInput.Text)
        };
        candidate.Validate();
        foreach (var text in new[] { candidate.Hotkeys.Replace, candidate.Hotkeys.Toggle, candidate.Hotkeys.Copy })
            if (!HotkeyService.TryParse(text, out _, out _)) throw new ArgumentException("快捷键格式无效，例如 Ctrl+Alt+Enter。");
        return candidate;
    }
    private static string[] Split(string text) => text.Split([',', '，', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = ReadSettings(); var warning = save(config, KeyInput.Password); KeyInput.Clear(); RefreshKeyStatus();
            ShowMessage(warning ?? (config.PrivacyConsent ? "已保存。仅监控允许列表中的应用。" : "已保存；未授权，所有文本读取保持暂停。"));
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
    private void RefreshStatistics()
    {
        var total = statistics.Successes + statistics.CacheHits;
        StatisticsText.Text = UiText.Format("HTTP 请求 {0} · 本地命中 / 节省请求 {1}\n翻译成功 {2} · 失败 {3} · 命中率 {4}%\n平均 API 延迟 {5} ms · 输入词元 {6} / 输出词元 {7}",
            statistics.ApiCalls, statistics.CacheHits, statistics.Successes, statistics.Failures, total == 0 ? 0 : statistics.CacheHits * 100 / total,
            statistics.AverageLatencyMilliseconds.ToString("F0", CultureInfo.InvariantCulture), statistics.PromptTokens, statistics.CompletionTokens);
    }
}
