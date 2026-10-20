using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
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
        if (args.Length == 2 && args[0] == "--webview-fixture") return WebViewFixture.Run(args[1]);
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-smoke"); Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), "TypeLingo-UiSmoke-" + Guid.NewGuid().ToString("N"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var checks = new List<string>();
        // Main 以 DispatcherFrame 驱动验收；程序化点击也必须具备真实 WPF 事件的同步上下文。
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        // Dispatcher 和退出清理中的异常可能不经过 Main 的 catch，仍需在 CI 中保留真实原因。
        void ReportUnhandled(object? error)
        {
            var message = "Last successful check: " + checks.LastOrDefault() + "\n" + error;
            try { File.WriteAllText(Path.Combine(output, "ui-unhandled.txt"), message); } catch (Exception) { }
            Console.Error.WriteLine("::error::" + message.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A"));
        }
        app.DispatcherUnhandledException += (_, e) => ReportUnhandled(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportUnhandled(e.ExceptionObject);
        try
        {
            app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/TypeLingo;component/Styles/Theme.xaml") };
            var settings = new SettingsService(temporary, new WindowsProtector());
            var favorites = new FavoriteStore(temporary, new WindowsProtector());
            using var vocabulary = new VocabularyStore(temporary, new WindowsProtector());
            InputFixture.WaitWithDispatcher(vocabulary.InitializeAsync().ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            settings.Save(new AppSettings());
            var window = new SettingsWindow(settings, new SessionStatistics(), (candidate, key) => { settings.Save(candidate, key); return null; },
                (_, _) => Task.FromResult<IReadOnlyList<string>>(["test-model"]), (_, _) => Task.FromResult(new TranslationResult("仅供界面测试的固定结果")), () => Task.CompletedTask, favorites, vocabulary);
            T Find<T>(string name) where T : FrameworkElement => (T)(window.FindName(name) ?? throw new InvalidOperationException("找不到控件：" + name));
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); checks.Add(message); Console.WriteLine("通过：" + message); }
            void SelectPage(string id)
            {
                var navigation = Find<ListBox>("NavigationList");
                navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().First(x => (string)x.Tag == id); Pump(window);
            }
            window.Show(); Pump(window);
            Check((string)((ListBoxItem)Find<ListBox>("NavigationList").Items[0]).Tag == "home" && Find<FrameworkElement>("PageHome").Visibility == Visibility.Visible, "主页位于导航顶部并默认打开");
            Check(window.IsVisible && window.ActualWidth >= 760, "真实 WPF 设置窗口已启动并完成布局");
            var navigation = Find<ListBox>("NavigationList");
            var items = navigation.Items.Cast<ListBoxItem>().ToArray();
            Rect Bounds(FrameworkElement element, Visual parent) => element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
            items[0].Focus(); Pump(window);
            var rowBounds = items.Select(item => Bounds(item, navigation)).ToArray();
            var labelBounds = items.Select(item => Bounds(((StackPanel)item.Content).Children.OfType<TextBlock>().Single(), navigation)).ToArray();
            var viewportWidth = Find<ScrollViewer>("PageScroll").ViewportWidth;
            foreach (var item in items)
            {
                item.Focus(); SelectPage((string)item.Tag);
                Check(items.Select((row, i) => Bounds(row, navigation) == rowBounds[i] &&
                    Bounds(((StackPanel)row.Content).Children.OfType<TextBlock>().Single(), navigation) == labelBounds[i]).All(x => x),
                    "切换并聚焦 Tab 不改变导航行与文字坐标：" + item.Tag);
                Check(Math.Abs(Find<ScrollViewer>("PageScroll").ViewportWidth - viewportWidth) < .1,
                    "不同页面保持相同内容视口宽度：" + item.Tag);
            }
            SelectPage("home");
            var search = Find<TextBox>("SettingsSearchInput"); search.Focus(); search.Clear(); Pump(window);
            Check(Find<TextBlock>("SearchPlaceholder").Visibility == Visibility.Collapsed && search.CaretIndex == 0,
                "空搜索框聚焦时隐藏占位文字，光标不与占位文字重叠");
            search.Text = "密钥"; search.CaretIndex = 0; Pump(window);
            var caret = search.GetRectFromCharacterIndex(0);
            Check(!caret.IsEmpty && caret.Left < 24 && caret.Height > 8, "真实搜索光标位于输入区左侧");
            search.Clear(); items[0].Focus(); Pump(window);
            Check(Find<TextBlock>("SearchPlaceholder").Visibility == Visibility.Visible, "空搜索框失焦后恢复占位提示");
            SelectPage("home");
            using (var icon = Application.GetResourceStream(new Uri("pack://application:,,,/TypeLingo;component/Assets/typelingo.ico")).Stream)
                Check(window.Icon != null && icon.Length > 0, "设置窗口与托盘使用可加载的新图标资源");
            var homeLogo = Find<Image>("HomeBrandImage");
            Check(homeLogo.Source is BitmapSource bitmap && bitmap.PixelWidth == 512 && homeLogo.Width == 58,
                "首页加载 C 方案图标原始资源并完整显示");
            foreach (var darkTaskbar in new[] { false, true })
            {
                using var trayIcon = BrandAssets.CreateTrayIcon(darkTaskbar);
                using var pixels = trayIcon.ToBitmap();
                var solid = new List<System.Drawing.Color>(); var clear = 0;
                for (var y = 0; y < pixels.Height; y++)
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var pixel = pixels.GetPixel(x, y);
                        if (pixel.A > 128) solid.Add(pixel);
                        if (pixel.A == 0) clear++;
                    }
                Check(solid.Count > 80 && clear > 80 && solid.All(c => darkTaskbar ? c.R > 240 && c.G > 240 && c.B > 240 : c.R < 40 && c.G < 40 && c.B < 40),
                    darkTaskbar ? "深色任务栏加载透明白色 C 托盘图标" : "浅色任务栏加载透明黑色 C 托盘图标");
            }
            var publishedExe = Path.GetFullPath("artifacts/win-x64/TypeLingo.exe");
            if (File.Exists(publishedExe))
            {
                using var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(publishedExe);
                Check(exeIcon != null && exeIcon.Width >= 16, "Windows 可从自包含程序提取已嵌入的 C 图标");
            }
            Check(Find<RadioButton>("SystemThemeRadio").IsChecked == true, "默认主题可跟随 Windows 系统");
            Check((string)Find<ComboBox>("TriggerInput").SelectedValue == "selection", "默认以鼠标划词触发，输入自动预览保留为可选项");
            Check(Find<CheckBox>("PreferOfflineCheck").IsChecked == true && Find<CheckBox>("AutoLearnCheck").IsChecked == true && Find<CheckBox>("OnlineFallbackCheck").IsChecked == true,
                "离线优先、在线回退和自动积累默认开启");
            SelectPage("vocabulary");
            Check(Find<TextBlock>("VocabularyCountText").Text.Contains("32159") && Find<TextBlock>("VocabularyCountText").Text.Contains("2441"),
                "真实设置页面加载用户词库可用数和歧义数");
            var uiRequest = TranslationRequest.Create(new AppSettings { Translation = new() { SourceLanguage = "zh", TargetLanguage = "en" } }, "ui-fixture-original");
            InputFixture.WaitWithDispatcher(vocabulary.LearnAsync(uiRequest, new TranslationResult("ui-fixture-answer"), default).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            Find<TextBox>("VocabularySearchInput").Text = "ui-fixture-original"; Pump(window);
            var vocabularyList = Find<ListBox>("VocabularyList");
            Check(vocabularyList.Items.Count == 1, "题库搜索定位实际在线积累条目");
            vocabularyList.SelectedIndex = 0; Pump(window);
            Find<TextBox>("VocabularyEditTranslation").Text = "manually reviewed fixture answer";
            Find<Button>("VocabularyEditButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            InputFixture.WaitWithDispatcher(Task.Run(async () => {
                for (var attempt = 0; attempt < 50; attempt++)
                { if ((await vocabulary.FindAsync(uiRequest, default))?.Text == "manually reviewed fixture answer") return true; await Task.Delay(100); }
                throw new InvalidOperationException("题库编辑未持久化");
            })); Pump(window);
            Check(Find<TextBlock>("MessageText").Text.Contains("自动积累不会覆盖人工编辑"), "通过实际编辑按钮保存 DPAPI 个人译文");
            Find<CheckBox>("PreferOfflineCheck").IsChecked = false; Find<CheckBox>("AutoLearnCheck").IsChecked = false; Find<CheckBox>("OnlineFallbackCheck").IsChecked = false;
            Find<RadioButton>("DarkThemeRadio").IsChecked = true; Pump(window);
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color.R < 100 && ((SolidColorBrush)window.Foreground).Color.R > 240, "深色主题卡片即时切换实际资源");
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
            Find<ComboBox>("PopupDisplayInput").SelectedValue = "result";
            Check(model.Text == "my-selected-model", "新版下拉框保留手动模型输入功能");
            Find<Button>("SaveSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Check(!settings.Current.Overlay.ShowLoading, "Loading 开关可保存并持久化");
            Check(!settings.Current.Vocabulary.PreferOffline && !settings.Current.Vocabulary.AutoLearn && !settings.Current.Vocabulary.AllowOnlineFallback,
                "离线优先、在线回退和自动积累可独立保存");
            Check(settings.Current.MonitorAllApplications && settings.Current.AI.Model == "my-selected-model" && settings.Current.Translation.TargetLanguage == "en" && settings.Current.Overlay.TriggerMode == "hotkey" && settings.Current.Overlay.Theme == "light", "保存设置同时持久化应用范围、模型、方向、主题与触发方式");
            SelectPage("general"); var scroll = Find<ScrollViewer>("PageScroll");
            scroll.ScrollToEnd(); Pump(window);
            Check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0, "新样式保持设置内容的滚动功能");
            scroll.ScrollToTop();
            foreach (var id in new[] { "general", "vocabulary", "ai", "privacy", "appearance" })
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
            var overlay = new OverlayWindow(new ReplacementService(new NoInputTarget(), coordinator), () => previewSettings, p => favorites.Add(p));
            var snapshot = new InputSnapshot("preview", "输入原文", new(50, 50, 20, 20));
            var preview = new Preview(snapshot, TranslationRequest.Create(previewSettings, snapshot.Text), new TranslationResult("Hello."), 1, false);
            overlay.Display(preview); Pump(overlay);
            Check(((TextBlock)overlay.FindName("OriginText")).Text == "在线翻译", "悬浮窗明确显示译文来源");
            ((Button)overlay.FindName("FavoriteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(favorites.GetAll().Count == 1 && !((Button)overlay.FindName("FavoriteButton")).IsEnabled, "星标主动收藏译文并防止重复点击");
            Check(overlay.ActualWidth <= 320 && overlay.ActualHeight <= 64, "短译文悬浮窗自动收窄且高度不超过 64 像素");
            Check(overlay.Opacity == 1 && ((SolidColorBrush)((Border)overlay.FindName("Bubble")).Background).Color.A < 255, "半透明只作用于背景，文字保持清晰");
            Capture(overlay, Path.Combine(output, "overlay-compact.png"));
            overlay.Display(preview with { Snapshot = snapshot with { CanWrite = false, IsSelection = true }, Result = new TranslationResult("只翻译选中的文字。") }); Pump(overlay);
            Check(((Button)overlay.FindName("ReplaceButton")).Visibility == Visibility.Collapsed, "选区预览不显示替换正文按钮");
            Capture(overlay, Path.Combine(output, "overlay-selection.png"));
            var offlineConfig = previewSettings with { Translation = new() { SourceLanguage = "auto", TargetLanguage = "ja" } };
            using (var offlineCoordinator = new TranslationCoordinator(new NoApiProvider(), new EmptyCache(), () => offlineConfig, new SessionStatistics(), vocabulary))
            {
                InputFixture.WaitWithDispatcher(offlineCoordinator.ObserveAsync(snapshot with { Text = "米饭", CanWrite = false, IsSelection = true }).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
                var offline = offlineCoordinator.CurrentPreview;
                Check(offline?.Result.Text == "ご飯" && offline.Origin == TranslationOrigin.Vocabulary, "未提供 API Key 时真实内置词库完成自动源语言离线翻译，在线服务禁止调用");
                overlay.Display(offline); Pump(overlay);
                Check(((TextBlock)overlay.FindName("OriginText")).Text == "离线题库" && ((Button)overlay.FindName("ReplaceButton")).Visibility == Visibility.Collapsed,
                    "离线阅读选区显示来源并隐藏替换按钮");
                Capture(overlay, Path.Combine(output, "overlay-offline.png"));
            }
            overlay.Display(preview with { Result = new TranslationResult(string.Concat(Enumerable.Repeat("较长译文会自动换行并限制可见高度。", 40))) }); Pump(overlay);
            Check(overlay.ActualWidth <= 320 && overlay.ActualHeight <= 190 && ((ScrollViewer)overlay.FindName("TranslationScroll")).ScrollableHeight > 0, "长译文保持紧凑并通过滚动显示");
            Capture(overlay, Path.Combine(output, "overlay-long.png"));
            overlay.Dismiss(); Check(!overlay.IsVisible, "关闭图标可隐藏悬浮窗");
            overlay.Loading(snapshot, "正在翻译…"); Pump(overlay);
            Check(overlay.IsVisible && ((ProgressBar)overlay.FindName("LoadingProgress")).Visibility == Visibility.Visible, "启用 Loading 时显示实际进度控件");
            overlay.Dismiss(); previewSettings = previewSettings with { Overlay = previewSettings.Overlay with { ShowLoading = false } };
            overlay.Loading(snapshot, "正在翻译…"); Pump(overlay);
            Check(!overlay.IsVisible, "关闭 Loading 时等待期间不显示悬浮窗");
            overlay.Loading(snapshot, "API 测试错误"); Pump(overlay);
            Check(overlay.IsVisible && ((TextBlock)overlay.FindName("TranslationText")).Text == "翻译暂不可用", "请求失败时显示简短提示并保留悬停错误原因");
            overlay.Dismiss();
            window.Show(); SelectPage("favorites");
            Check(Find<ListBox>("FavoritesList").Items.Count == 1, "收藏页加载本地条目");
            Find<Button>("StartReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Check(Find<TextBlock>("ReviewTranslationText").Visibility == Visibility.Collapsed, "复习先展示原文并隐藏答案");
            Find<Button>("ShowAnswerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Check(Find<TextBlock>("ReviewTranslationText").Visibility == Visibility.Visible, "主动显示复习答案");
            Capture(window, Path.Combine(output, "favorites-review.png"));
            Find<Button>("ReviewRememberedButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Check(favorites.GetAll()[0].ReviewCount == 1 && favorites.GetDue().Count == 0, "记住后保存复习排期");
            window.Hide();
            using (var clickCoordinator = new TranslationCoordinator(new LocalProvider(), new EmptyCache(), () => previewSettings, new SessionStatistics()))
            {
                var target = new LocalTarget(snapshot);
                var clickable = new OverlayWindow(new ReplacementService(target, clickCoordinator), () => previewSettings);
                InputFixture.WaitWithDispatcher(clickCoordinator.ObserveAsync(snapshot).ContinueWith(_ => true));
                clickable.Display(clickCoordinator.CurrentPreview); Pump(clickable);
                ((TextBlock)clickable.FindName("TranslationText")).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); Pump(clickable);
                Check(((FrameworkElement)clickable.FindName("Confirmation")).Visibility == Visibility.Visible && target.Writes == 0, "点击译文进入确认且不会提前写入");
                ((Button)clickable.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(clickable);
                Check(target.Writes == 1 && !clickable.IsVisible, "确认后点击译文流程只写入一次并隐藏");
            }
            var fixtureErrors = new List<Exception>();
            try { InputFixture.Verify(Check); } catch (Exception error) { fixtureErrors.Add(error); }
            try { WebViewFixture.Verify(Check, (w, name) => Capture(w, Path.Combine(output, name)), output); }
            catch (Exception error) { fixtureErrors.Add(error); }
            File.WriteAllLines(Path.Combine(output, "ui-validation.txt"), checks);
            if (fixtureErrors.Count > 0) throw new AggregateException("跨进程选区验收失败", fixtureErrors);
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
    private sealed class LocalProvider : ITranslationProvider
    { public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) => Task.FromResult(new TranslationResult("本地测试译文")); }
    private sealed class LocalTarget(InputSnapshot snapshot) : IInputTarget
    {
        public int Writes;
        public Task<InputSnapshot?> CaptureAsync(CancellationToken token) => Task.FromResult<InputSnapshot?>(snapshot);
        public Task<bool> TryReplaceAsync(InputSnapshot expected, string translation, CancellationToken token)
        { if (!snapshot.SameContent(expected)) return Task.FromResult(false); Writes++; return Task.FromResult(true); }
    }
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
