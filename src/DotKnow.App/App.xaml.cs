using System.Net.Http;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using DotKnow.App.Windows;
using DotKnow.Core;
using DotKnow.Infrastructure;
using Forms = System.Windows.Forms;

namespace DotKnow.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private SettingsService settings = null!;
    private CacheService cache = null!;
    private VocabularyStore vocabulary = null!;
    private TranslationCoordinator coordinator = null!;
    private InputObserver observer = null!;
    private OverlayWindow overlay = null!;
    private AssistantWindow assistant = null!;
    private SettingsWindow settingsWindow = null!;
    private HotkeyService hotkeys = null!;
    private System.Drawing.Icon? trayIconWhite;
    private System.Drawing.Icon? trayIconBlack;
    private Forms.NotifyIcon tray = null!;
    private Forms.ToolStripMenuItem pauseItem = null!;
    private Forms.ToolStripMenuItem examItem = null!;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private InputSnapshot? latest;
    private readonly SelectionDismissal dismissal = new();
    private bool capturingScreenshot;
    private CancellationTokenSource? screenshotCancellation;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\TypeLingo.CurrentUser", out var first);
        if (!first) { System.Windows.MessageBox.Show(UiText.Get("点解（或旧版伴译）已在系统托盘运行。")); Shutdown(); return; }
        var protector = new DpapiProtector();
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeLingo");
        settings = new SettingsService(folder, protector); settings.Load();
        UiText.Apply(settings.Current.UiLanguage);
        var statistics = new SessionStatistics();
        cache = new CacheService(() => settings.Current, System.IO.Path.Combine(folder, "translations.db"), protector);
        vocabulary = new VocabularyStore(folder, protector);
        var provider = new ProviderApi(http, () => settings.Current, settings.ReadKey, statistics);
        coordinator = new TranslationCoordinator(provider, cache, () => settings.Current, statistics, vocabulary);
        observer = new InputObserver(() =>
        {
            var config = settings.Current;
            // A screenshot session owns its source. Suspend UIA/clipboard polling until it is closed.
            return capturingScreenshot || assistant?.SessionImage != null
                ? config with { Translation = config.Translation with { Enabled = false } } : config;
        });
        var replacement = new ReplacementService(observer, coordinator);
        var favorites = new FavoriteStore(folder, protector);
        overlay = new OverlayWindow(replacement, () => settings.Current, preview => favorites.Add(preview));
        assistant = new AssistantWindow(() => settings.Current, preview => favorites.Add(preview));
        assistant.QueryRequested += async (snapshot, request) =>
        {
            var revision = assistant.QueryRevision;
            try { await coordinator.ObserveAsync(snapshot, true, request); }
            finally { if (assistant.QueryRevision == revision) assistant.CompleteQuery(); }
        };
        assistant.Dismissed += DismissSelection;
        overlay.Dismissed += DismissSelection;
        hotkeys = new HotkeyService();
        ProviderApi TestApi(AppSettings candidate, string newKey) => new(http, () => candidate,
            ai => string.IsNullOrWhiteSpace(newKey) ? settings.ReadKey(ai) : newKey.Trim(), statistics);
        settingsWindow = new SettingsWindow(settings, statistics, SaveSettings,
            (candidate, key) => TestApi(candidate, key).GetModelsAsync(),
            (candidate, key) => TestApi(candidate, key).TranslateAsync(TranslationRequest.Create(candidate, "Hello") with { SourceLanguage = "en" }, CancellationToken.None),
            async () => { await coordinator.ObserveAsync(null, true); await cache.ClearAsync(); }, favorites, vocabulary, () => { _ = coordinator.ObserveAsync(null, true); });
        settingsWindow.ScreenshotRequested += async () => await CaptureAndSolveAsync();
        _ = PrepareVocabularyAsync();
        coordinator.PreviewChanged += preview => Ui(() =>
        {
            // A result can have been queued on the dispatcher just before closing or starting a new screenshot.
            if (capturingScreenshot || preview != null && !coordinator.IsCurrent(preview)) return;
            if (assistant.HasSession) assistant.Display(preview); else overlay.Display(preview);
        });
        coordinator.StatusChanged += status => Ui(() =>
        {
            if (assistant.HasSession) assistant.ShowStatus(status);
            else { overlay.ShowStatus(status); overlay.Loading(latest, status); }
        });
        observer.SnapshotChanged += snapshot =>
        {
            var capturedConfig = settings.Current;
            Ui(() =>
            {
                if (!ReferenceEquals(capturedConfig, settings.Current)) return;
                if (capturingScreenshot || assistant.SessionImage != null) return;
                // Clicking our question box changes focus; its session keeps the already captured selection.
                if (assistant.HasSession && assistant.OwnsForeground) return;
                HandleSnapshot(snapshot);
            });
        };
        observer.StatusChanged += status => Ui(() =>
        {
            overlay.ShowStatus(status);
            if (status == "微信选区已读取，但剪贴板恢复失败，请检查剪贴板内容。")
                tray?.ShowBalloonTip(6000, "点解 · DotKnow", UiText.Get(status), Forms.ToolTipIcon.Warning);
        });
        hotkeys.Pressed += async id =>
        {
            if (capturingScreenshot && id != 2) return;
            if (id == 1 && !assistant.HasSession) await overlay.RequestReplacementAsync();
            else if (id == 2) TogglePause();
            else if (id == 3) { if (assistant.HasSession) assistant.CopyAnswer(); else overlay.CopyTranslation(); }
            else if (id == 4)
            {
                try
                {
                    if (assistant.HasSession && assistant.OwnsForeground) { assistant.Activate(); return; }
                    if (assistant.SessionImage != null)
                    {
                        assistant.CancelSession(false); latest = null;
                        await coordinator.ObserveAsync(null, true);
                    }
                    var snapshot = await observer.RequestPreviewAsync(CancellationToken.None);
                    HandleSnapshot(snapshot, true);
                    if (snapshot == null) tray?.ShowBalloonTip(4000, "点解 · DotKnow",
                        UiText.Get("未读到可查询的文字，请先选中可复制的文本，再按快捷键。"), Forms.ToolTipIcon.Info);
                }
                catch (Exception) { latest = null; await coordinator.ObserveAsync(null, true); overlay.ShowStatus("无法读取当前输入或选区，请切换应用后重试。"); }
            }
            else if (id == 5) await CaptureAndSolveAsync();
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置 / 隐私授权", null, (_, _) => ShowSettings()).Tag = "设置 / 隐私授权";
        menu.Items.Add("任务与提示词", null, (_, _) => { ShowSettings(); settingsWindow.OpenAssistantSettings(); }).Tag = "任务与提示词";
        menu.Items.Add("截图解题", null, async (_, _) => await CaptureAndSolveAsync()).Tag = "截图解题";
        examItem = new Forms.ToolStripMenuItem("考试模式", null, (_, _) => ToggleExamMode()) { Tag = "考试模式" }; menu.Items.Add(examItem);
        pauseItem = new Forms.ToolStripMenuItem("暂停全部读取", null, (_, _) => TogglePause()) { Tag = "暂停全部读取" }; menu.Items.Add(pauseItem);
        menu.Items.Add("清除缓存", null, async (_, _) =>
        {
            try { await coordinator.ObserveAsync(null, true); await cache.ClearAsync(); }
            catch (Exception) { ShowSettings(); }
        }).Tag = "清除缓存";
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => Shutdown()).Tag = "退出";
        trayIconWhite = BrandAssets.CreateTrayIcon(true);
        trayIconBlack = BrandAssets.CreateTrayIcon(false);
        tray = new Forms.NotifyIcon { Icon = DesktopTheme.IsTaskbarDark ? trayIconWhite : trayIconBlack,
            Text = "点解 · DotKnow", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        UiText.Changed += UpdateTray;
        DesktopTheme.Changed += UpdateTrayAppearance;
        ApplyTheme(); UpdateTray();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        var hotkeyError = hotkeys.Register(settings.Current.Hotkeys);
        if (hotkeyError != null) settingsWindow.ShowMessage(hotkeyError);
        if (!settings.Current.PrivacyConsent || (!settings.HasKey && (settings.Current.Assistant.PresetId != "translate" ||
            !settings.Current.Vocabulary.PreferOffline && settings.Current.Vocabulary.AllowOnlineFallback)) || settings.LoadWarning != null || hotkeyError != null) ShowSettings();
    }
    private async Task PrepareVocabularyAsync()
    {
        try { await vocabulary.InitializeAsync(); }
        catch (Exception) { Ui(() => settingsWindow.ShowMessage("离线题库无法初始化，原有文件已保留；请检查存储权限。")); }
    }
    private void HandleSnapshot(InputSnapshot? snapshot, bool force = false)
    {
        if (!dismissal.Allows(snapshot, force))
        {
            if (latest != null || assistant.HasSession || overlay.IsVisible)
            {
                latest = null; assistant.CancelSession(false); overlay.Dismiss(false);
                _ = coordinator.ObserveAsync(null, true);
            }
            return;
        }
        var same = latest == null ? snapshot == null : latest.SameContent(snapshot) && snapshot!.SelectionRevision <= latest.SelectionRevision;
        latest = snapshot;
        if (snapshot == null)
        {
            assistant.CancelSession(false); _ = coordinator.ObserveAsync(null, force); return;
        }
        if (!force && same) { overlay.MoveAnchor(snapshot.Bounds, snapshot.ControlBounds); return; }
        assistant.CancelSession(false); overlay.Dismiss(false);
        _ = coordinator.ObserveAsync(null, true);
        var config = settings.Current;
        if (config.Assistant.SelectionAction == "ask" || config.Assistant.PresetId == "ask")
            assistant.Present(snapshot, activate: force);
        else if (config.Assistant.PresetId != "translate")
            assistant.Present(snapshot, autoRun: true);
        else _ = coordinator.ObserveAsync(snapshot, force, AssistantPresets.CreateRequest(config, snapshot.Text));
    }
    private void DismissSelection(InputSnapshot? snapshot)
    {
        dismissal.Dismiss(snapshot ?? latest);
        latest = null;
        _ = coordinator.ObserveAsync(null, true);
    }
    private string? SaveSettings(AppSettings candidate, string key)
    {
        screenshotCancellation?.Cancel();
        settings.Save(candidate, key);
        latest = null; assistant.CancelSession(false); overlay.Dismiss(false);
        UiText.Apply(candidate.UiLanguage);
        _ = coordinator.ObserveAsync(null, true); ApplyTheme(); UpdateTray();
        return hotkeys.Register(candidate.Hotkeys);
    }
    private void TogglePause()
    {
        screenshotCancellation?.Cancel();
        settings.SetPaused(settings.Current.Translation.Enabled);
        latest = null; assistant.CancelSession(false);
        _ = coordinator.ObserveAsync(null, true); overlay.Hide(); UpdateTray();
    }
    internal void PauseMonitoring()
    {
        screenshotCancellation?.Cancel();
        settings.SetPaused(true);
        latest = null; assistant.CancelSession(false);
        _ = coordinator.ObserveAsync(null, true); overlay.Hide(); UpdateTray();
    }
    private void UpdateTray()
    {
        pauseItem.Checked = !settings.Current.Translation.Enabled;
        examItem.Checked = settings.Current.Assistant.ExamMode;
        tray.Text = UiText.Get(!settings.Current.Translation.Enabled || !settings.Current.PrivacyConsent ? "点解 · 已暂停读取" :
            settings.Current.MonitorAllApplications ? "点解 · 监控全部可访问应用" : "点解 · 仅监控允许的应用");
        foreach (Forms.ToolStripItem item in tray.ContextMenuStrip!.Items)
            if (item.Tag is string text) item.Text = UiText.Get(text);
    }
    private void ToggleExamMode()
    {
        try
        {
            var config = settings.Current;
            var warning = SaveSettings(config with { Assistant = config.Assistant with { ExamMode = !config.Assistant.ExamMode } }, "");
            if (warning != null) { ShowSettings(); settingsWindow.ShowMessage(warning); }
        }
        catch (Exception) { ShowSettings(); settingsWindow.ShowMessage("模式保存失败，请在任务与提示词中重试。"); }
    }
    private void ShowSettings()
    {
        dismissal.Dismiss(latest);
        assistant.CancelSession(false); _ = coordinator.ObserveAsync(null, true); overlay.Dismiss(false);
        settingsWindow.Show(); settingsWindow.Activate();
    }
    private void UpdateTrayAppearance()
    { if (tray?.ContextMenuStrip is { } menu) TrayAppearance.Apply(menu); }
    private void Ui(Action action)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Ui(() =>
    {
        if (settings.Current.Overlay.Theme == "system") ApplyTheme();
        if (tray != null) tray.Icon = DesktopTheme.IsTaskbarDark ? trayIconWhite : trayIconBlack;
    });
    internal void ApplyTheme(OverlaySettings? configuration = null)
    {
        var dark = DesktopTheme.ApplyResources(configuration ?? settings.Current.Overlay);
        if (settingsWindow != null) Native.ApplyWindowAppearance(new WindowInteropHelper(settingsWindow).Handle, dark);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        screenshotCancellation?.Cancel();
        observer?.Dispose(); coordinator?.Dispose(); hotkeys?.Dispose();
        tray?.Dispose(); trayIconWhite?.Dispose(); trayIconBlack?.Dispose(); http.Dispose();
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        DesktopTheme.Changed -= UpdateTrayAppearance;
        UiText.Changed -= UpdateTray;
        // 后台 API 与 UIA 调用可能仍在退出；进程结束会释放缓存文件句柄。
        instance?.Dispose(); base.OnExit(e);
    }

    private async Task CaptureAndSolveAsync()
    {
        if (capturingScreenshot) return;
        var config = settings.Current;
        if (!config.PrivacyConsent || !config.Translation.Enabled)
        {
            ShowSettings(); settingsWindow.OpenPrivacySettings();
            settingsWindow.ShowMessage("请先授权并开启助手，再使用截图解题。"); return;
        }
        var error = ProblemSolving.ConfigurationError(config.AI);
        if (error != null || !settings.HasKey)
        {
            ShowSettings(); settingsWindow.OpenAiSettings();
            settingsWindow.ShowMessage(error ?? "请为当前服务商和 API 地址保存密钥。"); return;
        }
        capturingScreenshot = true;
        using var cancellation = new CancellationTokenSource(); screenshotCancellation = cancellation;
        try
        {
            latest = null; assistant.CancelSession(false); overlay.Dismiss(false);
            await coordinator.ObserveAsync(null, true);
            settingsWindow.Hide();
            await Task.Delay(150, cancellation.Token);
            var captured = await ScreenshotCapture.CaptureAsync(cancellation.Token);
            if (captured == null || cancellation.IsCancellationRequested || !ReferenceEquals(config, settings.Current)) return;
            var snapshot = new InputSnapshot("screenshot-" + Guid.NewGuid().ToString("N"), captured.Image.Description, captured.Bounds,
                CanWrite: false, IsSelection: true);
            latest = snapshot;
            assistant.PresentScreenshot(snapshot, captured.Image);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e)
        {
            ShowSettings(); settingsWindow.ShowMessage(e is ArgumentException ? e.Message : "截图失败，请重新框选题目或检查屏幕访问权限。");
        }
        finally { screenshotCancellation = null; capturingScreenshot = false; }
    }
}
