using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TypeLingo.App;
using TypeLingo.App.Windows;
using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.UiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--input-fixture") return InputFixture.Run(args[1]);
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-smoke"); Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), "TypeLingo-UiSmoke-" + Guid.NewGuid().ToString("N"));
        var app = new App.App(); var checks = new List<string>();
        try
        {
            app.InitializeComponent();
            var settings = new SettingsService(temporary, new WindowsProtector());
            settings.Save(new AppSettings());
            var window = new SettingsWindow(settings, new SessionStatistics(), (candidate, key) => { settings.Save(candidate, key); return null; },
                (_, _) => Task.FromResult<IReadOnlyList<string>>(["test-model"]), (_, _) => Task.FromResult(new TranslationResult("仅供界面测试的固定结果")), () => Task.CompletedTask);
            T Find<T>(string name) where T : FrameworkElement => (T)(window.FindName(name) ?? throw new InvalidOperationException("找不到控件：" + name));
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); checks.Add(message); Console.WriteLine("通过：" + message); }
            void SelectPage(string id)
            {
                var navigation = Find<ListBox>("NavigationList");
                navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().First(x => (string)x.Tag == id); Pump(window);
            }
            window.Show(); Pump(window);
            Check(window.IsVisible && window.ActualWidth >= 760, "真实 WPF 设置窗口已启动并完成布局");
            Check(Find<RadioButton>("SystemThemeRadio").IsChecked == true, "默认主题可跟随 Windows 系统");
            Find<RadioButton>("DarkThemeRadio").IsChecked = true; Pump(window);
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color.R < 100, "深色主题卡片即时切换实际资源");
            Capture(window, Path.Combine(output, "settings-dark.png"));
            Find<RadioButton>("LightThemeRadio").IsChecked = true; Pump(window);
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color.R > 240, "浅色主题恢复白色设置界面");
            Find<ComboBox>("TriggerInput").SelectedValue = "hotkey";
            Check(HotkeyService.TryParse("Ctrl+Alt+T", out _, out _), "新的预览快捷键格式有效");
            SelectPage("privacy");
            Check(Find<CheckBox>("AllApplicationsCheck").IsChecked == true, "默认启用所有应用模式");
            Check(Find<FrameworkElement>("AllowedApplicationsCard").Visibility == Visibility.Collapsed, "所有应用模式不要求填写允许列表");
            Find<CheckBox>("AllApplicationsCheck").IsChecked = false; Pump(window);
            Check(Find<FrameworkElement>("AllowedApplicationsCard").Visibility == Visibility.Visible, "切换到指定应用后显示允许列表");
            Find<CheckBox>("AllApplicationsCheck").IsChecked = true;
            Find<TextBox>("SettingsSearchInput").Text = "密钥"; Pump(window);
            Check(Find<FrameworkElement>("PageAi").Visibility == Visibility.Visible, "搜索密钥可定位 AI 设置页");
            Find<ComboBox>("UiLanguageInput").SelectedValue = "en-US"; Pump(window);
            Check(Find<TextBlock>("PageTitleText").Text == "AI services & models", "切换英语后导航标题即时本地化");
            Find<ComboBox>("UiLanguageInput").SelectedValue = "zh-CN";
            Find<TextBox>("SettingsSearchInput").Text = "不存在的设置 xyzzy"; Pump(window);
            Check(Find<TextBlock>("NoResultsText").Visibility == Visibility.Visible, "无匹配项时显示空结果状态");
            Find<TextBox>("SettingsSearchInput").Clear(); SelectPage("general");
            Find<ComboBox>("PresetInput").SelectedValue = "zh-en"; Pump(window);
            Check((string)Find<ComboBox>("SourceInput").SelectedValue == "zh" && (string)Find<ComboBox>("TargetInput").SelectedValue == "en", "翻译方向预设联动仍然正确");
            SelectPage("ai"); var model = Find<ComboBox>("ModelInput"); model.ApplyTemplate();
            var editor = (TextBox)(model.Template.FindName("PART_EditableTextBox", model) ?? throw new InvalidOperationException("模型编辑模板未连接"));
            editor.Text = "my-selected-model"; Pump(window);
            Check(model.Text == "my-selected-model", "新版下拉框保留手动模型输入功能");
            Find<Button>("SaveSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Check(settings.Current.MonitorAllApplications && settings.Current.AI.Model == "my-selected-model" && settings.Current.Translation.TargetLanguage == "en" && settings.Current.Overlay.TriggerMode == "hotkey" && settings.Current.Overlay.Theme == "light", "保存设置同时持久化应用范围、模型、方向、主题与触发方式");
            SelectPage("general"); var scroll = Find<ScrollViewer>("PageScroll");
            scroll.ScrollToEnd(); Pump(window);
            Check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0, "新样式保持设置内容的滚动功能");
            scroll.ScrollToTop();
            foreach (var id in new[] { "general", "ai", "privacy", "appearance" })
            {
                SelectPage(id); Capture(window, Path.Combine(output, "settings-" + id + ".png"));
            }
            window.Width = 820; window.Height = 560; SelectPage("general"); Pump(window);
            var save = Find<Button>("SaveSettingsButton"); var origin = save.TranslatePoint(new Point(), (UIElement)window.Content);
            Check(origin.Y + save.ActualHeight <= ((FrameworkElement)window.Content).ActualHeight, "较小窗口仍完整显示固定保存按钮");
            Capture(window, Path.Combine(output, "settings-small.png"));
            window.Hide();
            var previewSettings = new AppSettings { PrivacyConsent = true, Overlay = new() { Theme = "light", AutoHideSeconds = 0 } };
            using var coordinator = new TranslationCoordinator(new NoApiProvider(), new EmptyCache(), () => previewSettings, new SessionStatistics());
            var overlay = new OverlayWindow(new ReplacementService(new NoInputTarget(), coordinator), () => previewSettings);
            var snapshot = new InputSnapshot("preview", "输入原文", new(50, 50, 20, 20));
            var preview = new Preview(snapshot, TranslationRequest.Create(previewSettings, snapshot.Text), new TranslationResult("Hello."), 1, false);
            overlay.Display(preview); Pump(overlay);
            Check(overlay.ActualWidth <= 320 && overlay.ActualHeight <= 64, "短译文悬浮窗自动收窄且高度不超过 64 像素");
            Check(overlay.Opacity == 1 && ((SolidColorBrush)((Border)overlay.FindName("Bubble")).Background).Color.A < 255, "半透明只作用于背景，文字保持清晰");
            Capture(overlay, Path.Combine(output, "overlay-compact.png"));
            overlay.Display(preview with { Snapshot = snapshot with { CanWrite = false, IsSelection = true }, Result = new TranslationResult("只翻译选中的文字。") }); Pump(overlay);
            Check(((Button)overlay.FindName("ReplaceButton")).Visibility == Visibility.Collapsed, "选区预览不显示替换正文按钮");
            Capture(overlay, Path.Combine(output, "overlay-selection.png"));
            overlay.Display(preview with { Result = new TranslationResult(string.Concat(Enumerable.Repeat("较长译文会自动换行并限制可见高度。", 40))) }); Pump(overlay);
            Check(overlay.ActualWidth <= 320 && overlay.ActualHeight <= 190, "长译文保持紧凑并通过滚动显示");
            Capture(overlay, Path.Combine(output, "overlay-long.png"));
            overlay.Dismiss(); Check(!overlay.IsVisible, "关闭图标可隐藏悬浮窗");
            InputFixture.Verify(Check);
            File.WriteAllLines(Path.Combine(output, "ui-validation.txt"), checks);
             Console.WriteLine("WPF 界面检查全部通过：" + checks.Count); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "ui-validation-failed.txt"), error.ToString());
            Console.Error.WriteLine("::error::" + error.ToString().Replace("\r", "").Replace("\n", "%0A")); return 1;
        }
        finally { app.Shutdown(); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout(); var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    private static void Capture(Window window, string file)
    {
        // 直接渲染已布局的真实 WPF 视觉树，不以网页或静态示意图代替应用截图。
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private sealed class NoApiProvider : ITranslationProvider
    { public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) => throw new InvalidOperationException("界面检查不应调用 API"); }
    private sealed class EmptyCache : ITranslationCache
    {
        public Task<TranslationResult?> GetAsync(string key, CancellationToken token) => Task.FromResult<TranslationResult?>(null);
        public Task PutAsync(string key, TranslationResult value, CancellationToken token) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class NoInputTarget : IInputTarget
    {
        public Task<InputSnapshot?> CaptureAsync(CancellationToken token) => Task.FromResult<InputSnapshot?>(null);
        public Task<bool> TryReplaceAsync(InputSnapshot snapshot, string text, CancellationToken token) => Task.FromResult(false);
    }
    private sealed class WindowsProtector : IDataProtector
    {
        public byte[] Protect(byte[] value) => ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser);
        public byte[] Unprotect(byte[] value) => ProtectedData.Unprotect(value, null, DataProtectionScope.CurrentUser);
    }
}
