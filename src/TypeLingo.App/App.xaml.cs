using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using TypeLingo.App.Windows;
using TypeLingo.Core;
using TypeLingo.Infrastructure;
using Forms = System.Windows.Forms;

namespace TypeLingo.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private SettingsService settings = null!;
    private CacheService cache = null!;
    private TranslationCoordinator coordinator = null!;
    private InputObserver observer = null!;
    private OverlayWindow overlay = null!;
    private SettingsWindow settingsWindow = null!;
    private HotkeyService hotkeys = null!;
    private Forms.NotifyIcon tray = null!;
    private Forms.ToolStripMenuItem pauseItem = null!;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private InputSnapshot? latest;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\TypeLingo.CurrentUser", out var first);
        if (!first) { System.Windows.MessageBox.Show(UiText.Get("TypeLingo 已在系统托盘运行。")); Shutdown(); return; }
        var protector = new DpapiProtector();
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeLingo");
        settings = new SettingsService(folder, protector); settings.Load();
        UiText.Apply(settings.Current.UiLanguage);
        var statistics = new SessionStatistics();
        cache = new CacheService(() => settings.Current, System.IO.Path.Combine(folder, "translations.db"), protector);
        var provider = new ProviderApi(http, () => settings.Current, settings.ReadKey, statistics);
        coordinator = new TranslationCoordinator(provider, cache, () => settings.Current, statistics);
        observer = new InputObserver(() => settings.Current);
        var replacement = new ReplacementService(observer, coordinator);
        overlay = new OverlayWindow(replacement, () => settings.Current);
        hotkeys = new HotkeyService();
        ProviderApi TestApi(AppSettings candidate, string newKey) => new(http, () => candidate,
            ai => string.IsNullOrWhiteSpace(newKey) ? settings.ReadKey(ai) : newKey.Trim(), statistics);
        settingsWindow = new SettingsWindow(settings, statistics, SaveSettings,
            (candidate, key) => TestApi(candidate, key).GetModelsAsync(),
            (candidate, key) => TestApi(candidate, key).TranslateAsync(TranslationRequest.Create(candidate, "Hello") with { SourceLanguage = "en" }, CancellationToken.None),
            async () => { await coordinator.ObserveAsync(null, true); await cache.ClearAsync(); });
        coordinator.PreviewChanged += preview => Ui(() => overlay.Display(preview));
        coordinator.StatusChanged += status => Ui(() => { overlay.ShowStatus(status); overlay.Loading(latest, status); });
        observer.SnapshotChanged += snapshot => Ui(() =>
        {
            latest = snapshot;
            if (snapshot != null) overlay.MoveAnchor(snapshot.Bounds);
            _ = coordinator.ObserveAsync(snapshot);
        });
        observer.StatusChanged += status => Ui(() => overlay.ShowStatus(status));
        hotkeys.Pressed += async id =>
        {
            if (id == 1) await overlay.RequestReplacementAsync();
            else if (id == 2) TogglePause();
            else if (id == 3) overlay.CopyTranslation();
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置 / 隐私授权", null, (_, _) => ShowSettings()).Tag = "设置 / 隐私授权";
        pauseItem = new Forms.ToolStripMenuItem("暂停全部读取", null, (_, _) => TogglePause()) { Tag = "暂停全部读取" }; menu.Items.Add(pauseItem);
        menu.Items.Add("清除缓存", null, async (_, _) =>
        {
            try { await coordinator.ObserveAsync(null, true); await cache.ClearAsync(); }
            catch (Exception) { ShowSettings(); }
        }).Tag = "清除缓存";
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => Shutdown()).Tag = "退出";
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "TypeLingo · 伴译", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        UiText.Changed += UpdateTray;
        ApplyTheme(); UpdateTray();
        var hotkeyError = hotkeys.Register(settings.Current.Hotkeys);
        if (hotkeyError != null) settingsWindow.ShowMessage(hotkeyError);
        if (!settings.Current.PrivacyConsent || !settings.HasKey || settings.LoadWarning != null || hotkeyError != null) ShowSettings();
    }
    private string? SaveSettings(AppSettings candidate, string key)
    {
        settings.Save(candidate, key);
        UiText.Apply(candidate.UiLanguage);
        _ = coordinator.ObserveAsync(null, true); ApplyTheme(); UpdateTray();
        return hotkeys.Register(candidate.Hotkeys);
    }
    private void TogglePause()
    {
        settings.SetPaused(settings.Current.Translation.Enabled);
        _ = coordinator.ObserveAsync(null, true); overlay.Hide(); UpdateTray();
    }
    internal void PauseMonitoring()
    {
        settings.SetPaused(true);
        _ = coordinator.ObserveAsync(null, true); overlay.Hide(); UpdateTray();
    }
    private void UpdateTray()
    {
        pauseItem.Checked = !settings.Current.Translation.Enabled;
        tray.Text = UiText.Get(!settings.Current.Translation.Enabled || !settings.Current.PrivacyConsent ? "TypeLingo · 已暂停读取" :
            settings.Current.MonitorAllApplications ? "TypeLingo · 监控全部可访问应用" : "TypeLingo · 仅监控允许的应用");
        foreach (Forms.ToolStripItem item in tray.ContextMenuStrip!.Items)
            if (item.Tag is string text) item.Text = UiText.Get(text);
    }
    private void ShowSettings() { settingsWindow.Show(); settingsWindow.Activate(); }
    private void Ui(Action action)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
    }
    private void ApplyTheme()
    {
        var dark = settings.Current.Overlay.DarkMode;
        var palette = new Dictionary<string, (string Light, string Dark)>
        {
            ["CanvasBrush"] = ("#F3F3F3", "#202020"), ["SurfaceBrush"] = ("#FFFFFF", "#2B2B2B"),
            ["CardBrush"] = ("#FBFBFB", "#2B2B2B"), ["BorderBrush"] = ("#E5E5E5", "#3A3A3A"),
            ["HoverBrush"] = ("#F5F5F5", "#343434"), ["SelectionBrush"] = ("#E9E9E9", "#383838"),
            ["TextBrush"] = ("#1A1A1A", "#F5F5F5"), ["MutedBrush"] = ("#666666", "#B8B8B8"),
            ["AccentBrush"] = ("#0067C0", "#60CDFF"), ["AccentTextBrush"] = ("#FFFFFF", "#00324A"),
            ["AccentSurfaceBrush"] = ("#E7F1FB", "#253641")
        };
        foreach (var pair in palette) Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? pair.Value.Dark : pair.Value.Light));
        Native.ApplyWindowAppearance(new WindowInteropHelper(settingsWindow).Handle, dark);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        observer?.Dispose(); coordinator?.Dispose(); hotkeys?.Dispose();
        tray?.Dispose(); http.Dispose();
        UiText.Changed -= UpdateTray;
        // 后台 API 与 UIA 调用可能仍在退出；进程结束会释放缓存文件句柄。
        instance?.Dispose(); base.OnExit(e);
    }
}
