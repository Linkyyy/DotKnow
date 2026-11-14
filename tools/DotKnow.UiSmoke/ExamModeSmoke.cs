using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DotKnow.App;
using DotKnow.App.Windows;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.UiSmoke;

internal static class ExamModeSmoke
{
    internal static void Verify(SettingsWindow settings, SettingsService service, CapturedProblem shot,
        Action<bool, string> check, Action<Window, string> capture)
    {
        settings.Show(); settings.OpenAssistantSettings(); Wait(30);
        var toggle = (CheckBox)settings.FindName("ExamModeCheck");
        check(toggle.IsChecked == true, "升级默认开启考试模式");
        toggle.IsChecked = false;
        ((Button)settings.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!service.Current.Assistant.ExamMode, "考试模式可在真实设置界面关闭并保存");
        toggle.IsChecked = true;
        ((Button)settings.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(service.Current.Assistant.ExamMode, "考试模式可重新开启并保存"); settings.Hide();

        var config = service.Current with { Assistant = service.Current.Assistant with { PresetId = "solve", ExamMode = true },
            Overlay = service.Current.Overlay with { ShowLoading = true } };
        var assistant = new AssistantWindow(() => config);
        T Find<T>(string name) where T : FrameworkElement => (T)assistant.FindName(name);
        TranslationRequest? submitted = null; var calls = 0; var dismissals = 0;
        assistant.QueryRequested += (_, request) => { submitted = request; calls++; };
        assistant.Dismissed += _ => dismissals++;
        var snapshot = new InputSnapshot("exam-fixture", shot.Image.Description, shot.Bounds, CanWrite: false, IsSelection: true);
        var editor = new TextBox { Text = "复制不会改写这个输入框" };
        var source = new Window { Width = 420, Height = 180, Left = 50, Top = 50, Content = editor };
        try
        {
            source.Show(); source.Activate(); editor.Focus(); Wait(40);
            assistant.PresentScreenshot(snapshot, shot.Image); Wait(40);
            check(calls == 1 && Find<FrameworkElement>("CompactPanel").IsVisible && !Find<FrameworkElement>("StandardPanel").IsVisible &&
                assistant.WindowStyle == WindowStyle.None, "考试模式的截图查询只显示无标题栏的小窗口");
            assistant.Display(new(snapshot, submitted!, new("答案：4"), 1, false)); Wait(40);
            check(assistant.ActualWidth <= 320 && assistant.ActualHeight < 85 &&
                Find<TextBlock>("CompactAnswerText").Text == "答案：4" && !Find<Image>("ScreenshotPreview").IsVisible &&
                !Find<ComboBox>("TaskInput").IsVisible && !Find<TextBlock>("QueryStatus").IsVisible,
                "短答案自动缩到小气泡，不显示题目图片、预设、状态栏或大块留白");
            capture(assistant, "assistant-exam.png");
            var menu = Find<Button>("CompactAnswerButton").ContextMenu;
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Wait(30);
            check(Find<FrameworkElement>("StandardPanel").IsVisible && calls == 1 && assistant.SessionImage == shot.Image,
                "右键展开详情保留截图与答案，不产生额外 AI 请求");
            Find<Button>("CompactModeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(30);
            source.Activate(); editor.Focus(); Wait(30);
            var foreground = Native.GetForegroundWindow();
            ClickAnswer();
            check(System.Windows.Clipboard.GetText() == "答案：4" && !assistant.IsVisible && !assistant.HasSession && assistant.SessionImage == null && dismissals == 1,
                "点击答案一次复制并收起，释放截图并通知取消会话");
            check(Native.GetForegroundWindow() == foreground && editor.Text == "复制不会改写这个输入框", "点击复制不抢走原应用焦点，也不执行粘贴或替换");

            var longAnswer = string.Concat(Enumerable.Repeat("关键步骤：先列式，再代入数据，最后检查答案。\n", 100));
            assistant.PresentScreenshot(snapshot, shot.Image);
            assistant.Display(new(snapshot, submitted!, new(longAnswer), 2, false)); Wait(40);
            check(assistant.ActualWidth <= 320 && assistant.ActualHeight <= 240 &&
                Find<ScrollViewer>("CompactScroll").ScrollableHeight > 0, "长答案限制在小窗口内滚动，不撑满屏幕");
            capture(assistant, "assistant-exam-long.png"); ClickAnswer();
            check(System.Windows.Clipboard.GetText() == longAnswer, "长答案点击复制完整文本，包含未显示的部分");

            config = config with { Overlay = config.Overlay with { ShowLoading = false } };
            assistant.PresentScreenshot(snapshot, shot.Image); Wait(20);
            check(!assistant.IsVisible, "考试模式仍遵守仅在查询完成后显示的选项");
            const string error = "API 认证失败，请检查 Key 与账户权限。";
            assistant.ShowStatus(error); assistant.CompleteQuery(); Wait(30);
            check(assistant.IsVisible && Find<TextBlock>("CompactAnswerText").Text == UiText.Get(error) && assistant.ActualHeight <= 240,
                "考试模式的认证错误以简短文字显示，不会静默隐藏");
            var before = System.Windows.Clipboard.GetText(); ClickAnswer();
            check(assistant.HasSession && System.Windows.Clipboard.GetText() == before, "没有答案时点击不会覆盖剪贴板或假装复制成功");
            ((MenuItem)Find<Button>("CompactAnswerButton").ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(!assistant.HasSession && !assistant.IsVisible, "精简窗口也可通过右键关闭而不复制");
        }
        finally { assistant.CancelSession(false); source.Close(); }
        void ClickAnswer()
        {
            var button = Find<Button>("CompactAnswerButton");
            using var pointer = new PointerFixture();
            PointerFixture.Move(button.PointToScreen(new Point(Math.Min(12, button.ActualWidth / 2), Math.Min(8, button.ActualHeight / 2))));
            pointer.Press(); Wait(25); pointer.Release(); Wait(40);
        }
    }
    private static void Wait(int milliseconds) => InputFixture.WaitWithDispatcher(Task.Delay(milliseconds).ContinueWith(_ => true));
}
