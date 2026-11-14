using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DotKnow.App;
using DotKnow.App.Windows;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.UiSmoke;

internal static class ScreenshotSmoke
{
    internal static void Verify(SettingsWindow settings, SettingsService service, Action<bool, string> check, Action<Window, string> capture)
    {
        settings.Show(); settings.OpenAiSettings(); Wait(50);
        ((ComboBox)settings.FindName("ProviderInput")).SelectedValue = "OpenAI";
        ((TextBox)settings.FindName("ImageModelInput")).Text = "gpt-4.1";
        ((Button)settings.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(service.Current.AI.Provider == "OpenAI" && service.Current.AI.ImageModel == "gpt-4.1" && service.Current.AI.Model == "gpt-4.1-mini",
            "截图模型可独立于文字模型通过真实设置界面保存");
        var screenshots = 0;
        settings.ScreenshotRequested += OnRequest;
        void OnRequest() => screenshots++;
        ((Button)settings.FindName("HomeScreenshotButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)settings.FindName("ScreenshotButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(screenshots == 2 && HotkeyService.TryParse(new HotkeySettings().Screenshot, out _, out _), "首页与任务页截图入口和默认快捷键均可用");
        settings.ScreenshotRequested -= OnRequest; settings.Hide();

        var background = new SolidColorBrush(Color.FromRgb(210, 235, 250));
        var fixture = new Window { Left = 80, Top = 80, Width = 500, Height = 240, Topmost = true,
            Background = background, Content = new TextBlock { Text = "解题验收固定题目：2 + 2 = ?", FontSize = 28, Margin = new Thickness(35) } };
        fixture.Show(); fixture.Activate(); Wait(100);
        try
        {
            System.Windows.Clipboard.SetText("screenshot-must-not-overwrite-clipboard");
            var start = fixture.PointToScreen(new Point(15, 15)); var end = fixture.PointToScreen(new Point(440, 175));
            var task = ScreenshotCapture.CaptureAsync(); Wait(100);
            using (var pointer = new PointerFixture())
            {
                PointerFixture.Move(start); pointer.Press(); Wait(40);
                for (var i = 1; i <= 8; i++) { PointerFixture.Move(start + (end - start) * (i / 8d)); Wait(20); }
                pointer.Release(); Wait(40);
            }
            var shot = InputFixture.WaitWithDispatcher(task) ?? throw new InvalidOperationException("拖动截图未返回图片。");
            check(Math.Abs(shot.Bounds.Width - (end.X - start.X)) <= 2 && Math.Abs(shot.Bounds.Height - (end.Y - start.Y)) <= 2,
                $"真实鼠标框选只捕获所选区域，坐标使用物理屏幕像素：实际 {shot.Bounds}，预期 {start} → {end}");
            using var stream = new MemoryStream(Convert.FromBase64String(shot.Image.Base64Data));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit();
            check(bitmap.PixelWidth == shot.Image.Width && bitmap.PixelHeight == shot.Image.Height && bitmap.PixelWidth > 200,
                "截图是可解码的 PNG，传输尺寸与实际图片一致");
            var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0); var pixel = new byte[4];
            pixels.CopyPixels(new Int32Rect(4, 4, 1, 1), pixel, 4, 0);
            check(Math.Abs(pixel[0] - 250) < 10 && Math.Abs(pixel[1] - 235) < 10,
                "截图窗口先隐藏再读取像素，图片没有框选遮罩污染");
            check(System.Windows.Clipboard.GetText() == "screenshot-must-not-overwrite-clipboard", "截图不读写剪贴板");
            fixture.Hide();
            var config = service.Current with { PrivacyConsent = true };
            var assistant = new AssistantWindow(() => config);
            TranslationRequest? submitted = null; var calls = 0;
            assistant.QueryRequested += (_, request) => { submitted = request; calls++; };
            var snapshot = new InputSnapshot("screenshot-fixture", shot.Image.Description, shot.Bounds, CanWrite: false, IsSelection: true);
            assistant.PresentScreenshot(snapshot, shot.Image); Wait(50);
            check(calls == 1 && submitted?.TaskId == "solve" && submitted.Image == shot.Image && submitted.Model == "gpt-4.1" && !submitted.IsTranslation,
                "完成截图后自动提交一次解题请求，携带真实 PNG，不触发纯翻译");
            assistant.Display(new(snapshot, submitted!, new("题目：2 + 2 = ?\n步骤：把两个 2 相加。\n答案：4。"), 1, false));
            ((Expander)assistant.FindName("SourceExpander")).IsExpanded = true; Wait(50);
            check(((TextBox)assistant.FindName("AnswerText")).Text.Contains("答案：4") &&
                ((Image)assistant.FindName("ScreenshotPreview")).Source != null && ((Image)assistant.FindName("ScreenshotPreview")).IsVisible, "解题结果和题目图片可在同一窗口查看");
            capture(assistant, "assistant-solve.png");
            ((Button)assistant.FindName("FollowUpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)assistant.FindName("QuestionInput")).Text = "请用另一种方法解释";
            ((Button)assistant.FindName("SubmitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(calls == 2 && submitted!.Image?.Digest == shot.Image.Digest && submitted.Question.Contains("另一种"),
                "追问继续携带同一张截图与补充问题");
            assistant.CancelSession();
            check(assistant.SessionImage == null && ((Image)assistant.FindName("ScreenshotPreview")).Source == null && !assistant.IsVisible,
                "关闭解题面板释放截图并隐藏结果");
            var cancelled = ScreenshotCapture.CaptureAsync(); Wait(80);
            PointerFixture.Escape();
            check(InputFixture.WaitWithDispatcher(cancelled) == null && calls == 2, "Esc 取消截图，不产生图片或额外解题请求");
            using var cancellation = new CancellationTokenSource();
            var interrupted = ScreenshotCapture.CaptureAsync(cancellation.Token); cancellation.Cancel();
            check(InputFixture.WaitWithDispatcher(interrupted) == null, "暂停或配置变更取消框选，不提交截图");
        }
        finally { fixture.Close(); }
    }
    private static void Wait(int milliseconds) => InputFixture.WaitWithDispatcher(Task.Delay(milliseconds).ContinueWith(_ => true));
}
