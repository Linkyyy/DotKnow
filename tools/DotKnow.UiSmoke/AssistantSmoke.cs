using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DotKnow.App;
using DotKnow.App.Windows;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.UiSmoke;

internal static class AssistantSmoke
{
    internal static void Verify(SettingsWindow settingsWindow, SettingsService service, VocabularyStore vocabulary,
        Action<bool, string> check, Action<Window, string> capture, string directory)
    {
        T Find<T>(string name) where T : FrameworkElement => (T)settingsWindow.FindName(name);
        settingsWindow.Show(); settingsWindow.Activate();
        var navigation = Find<ListBox>("NavigationList");
        navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(x => (string)x.Tag == "prompts"); Pump(settingsWindow);
        check(Find<ComboBox>("AssistantTaskInput").SelectedValue as string == "encyclopedia" &&
            Find<ComboBox>("SelectionActionInput").SelectedValue as string == "ask", "新用户默认百科预设与先选择任务的划选即问模式");
        check(Find<TextBox>("PromptBodyInput").IsReadOnly, "内置预设可查看且不会被误修改");
        Find<Button>("NewPromptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<TextBox>("PromptNameInput").Text = "我的学习教练";
        Find<TextBox>("PromptBodyInput").Text = "请用简洁的中文解释，并用一个生活中的例子帮助理解。";
        Find<Button>("StorePromptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var customId = (string)Find<ListBox>("PromptList").SelectedValue;
        check(customId.StartsWith("custom-", StringComparison.Ordinal), "新建预设拥有独立标识且列表保持该自定义预设选中");
        Find<ComboBox>("AssistantTaskInput").SelectedValue = customId;
        Find<ComboBox>("SelectionActionInput").SelectedValue = "instant";
        Pump(settingsWindow);
        check(Find<ComboBox>("SelectionActionInput").ActualWidth < 200 && Find<ComboBox>("AssistantTaskInput").ActualWidth < 220,
            "短标签下拉框按所选文字收紧，不保留原来的整行固定宽度");
        Find<Button>("SaveSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(settingsWindow);
        var reloaded = new SettingsService(directory, new Protector()); reloaded.Load();
        check(reloaded.Current.Assistant.PresetId == customId && reloaded.Current.Assistant.SelectionAction == "instant" &&
            reloaded.Current.Assistant.CustomPresets.Single().Instructions.Contains("生活中的例子"), "自定义提示词和默认任务通过真实设置界面保存并可重载");
        Find<ScrollViewer>("PageScroll").ScrollToTop(); Pump(settingsWindow);
        capture(settingsWindow, "settings-prompts.png");
        Find<ScrollViewer>("PageScroll").ScrollToBottom(); Pump(settingsWindow);
        capture(settingsWindow, "settings-custom-prompt.png");
        Find<Button>("DeletePromptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>("SaveSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(settingsWindow);
        check(service.Current.Assistant.CustomPresets.Length == 0 && service.Current.Assistant.PresetId == "encyclopedia",
            "删除默认自定义预设后安全回退到百科");

        var config = new AppSettings { PrivacyConsent = true, Overlay = new() { Theme = "light" } };
        DesktopTheme.ApplyResources(config.Overlay);
        var favorites = new FavoriteStore(Path.Combine(directory, "assistant-favorites"), new Protector());
        var assistant = new AssistantWindow(() => config, p => favorites.Add(p));
        T Control<T>(string name) where T : FrameworkElement => (T)assistant.FindName(name);
        TranslationRequest? submitted = null; var submissions = 0;
        assistant.QueryRequested += (_, request) => { submitted = request; submissions++; };
        var selected = new InputSnapshot("assistant-fixture", "熵（entropy）", new(50, 50, 120, 24), CanWrite: false, IsSelection: true);
        assistant.Present(selected); Pump(assistant);
        var canvas = (Border)assistant.Content;
        var actions = Control<Button>("SaveAnswerButton").TransformToAncestor(canvas).TransformBounds(new Rect(Control<Button>("SaveAnswerButton").RenderSize));
        check(canvas.Background is System.Windows.Media.SolidColorBrush { Color.A: 255 } && actions.Right <= canvas.ActualWidth && actions.Bottom <= canvas.ActualHeight,
            "提问面板具有完整不透明背景且右侧操作按钮位于可见区域内");
        check(!assistant.IsActive && submissions == 0 && assistant.HasSession, "自动出现任务面板时保留应用焦点，未点击查询前不发送内容");
        Control<Button>("QuestionToggleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        assistant.Activate(); Control<TextBox>("QuestionInput").Focus(); Control<TextBox>("QuestionInput").Text = "请用生活中的例子解释。"; Pump(assistant);
        check(assistant.Matches(selected) && Control<TextBox>("QuestionInput").IsKeyboardFocusWithin, "点击问题框获得输入焦点后仍保留原应用选区");
        using (var observer = new InputObserver(() => config))
        {
            var recaptured = InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(default));
            check(recaptured == null && assistant.SessionSnapshot?.Text == "熵（entropy）", "UIA 不读取自己的问题框且不会将提问文字替换为查询选区");
        }
        capture(assistant, "assistant-question.png");
        Control<Button>("SubmitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(assistant);
        check(submissions == 1 && submitted is { TaskId: "encyclopedia", IsTranslation: false } &&
            submitted.Question == "请用生活中的例子解释。" && assistant.IsQuerying, "百科请求携带捕获选区和用户问题，进入可取消的查询状态");
        var answer = new Preview(selected, submitted!, new("熵，是描述系统可能状态数量的物理量。\n\n把一副排好的扑克牌打乱，通常比将它重新排好容易：杂乱的排列方式远多于整齐的排列方式。这个例子帮助理解概率意义上的熵。\n\n日常说的“混乱程度”只是直观类比，并不能替代严格定义。"), 1, false);
        assistant.Display(answer); Pump(assistant);
        check(!assistant.IsQuerying && Control<TextBox>("AnswerText").IsReadOnly && Control<Button>("CopyAnswerButton").IsEnabled,
            "回答可选择与滚动，完成后恢复任务输入");
        check(Control<StackPanel>("EditorPanel").Visibility == Visibility.Collapsed &&
            !Control<Expander>("SourceExpander").IsExpanded && assistant.ActualHeight <= 420,
            "百科结果默认收起任务编辑器与完整原文，使用简洁阅读界面");
        check(new[] { "CopyAnswerButton", "SaveAnswerButton", "FollowUpButton" }.All(name =>
        {
            var button = Control<Button>(name);
            var bounds = button.TransformToAncestor(canvas).TransformBounds(new Rect(button.RenderSize));
            return bounds.Right <= canvas.ActualWidth && bounds.Bottom <= canvas.ActualHeight;
        }), "百科阅读界面的复制、收藏和追问按钮均位于窗口内");
        Control<Button>("CopyAnswerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(System.Windows.Clipboard.GetText() == answer.Result.Text, "查询结果支持真实剪贴板复制");
        Control<Button>("SaveAnswerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(favorites.GetAll().Single() is { TaskId: "encyclopedia", Question: "请用生活中的例子解释。" }, "AI 回答收藏保留任务和用户问题");
        capture(assistant, "assistant-encyclopedia.png");
        Control<Button>("FollowUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(assistant);
        check(Control<StackPanel>("EditorPanel").Visibility == Visibility.Visible && Control<TextBox>("QuestionInput").IsVisible &&
            assistant.SessionSnapshot?.SameContent(selected) == true && Control<TextBox>("AnswerText").Text == answer.Result.Text,
            "追问按需展开输入，保留原选区和已经生成的回答");
        assistant.Display(answer with { Result = new(string.Concat(Enumerable.Repeat("较长的回答在面板中滚动阅读，不会把窗口撑出屏幕。\n", 70))) }); Pump(assistant);
        check(Control<TextBox>("AnswerText").ExtentHeight > Control<TextBox>("AnswerText").ViewportHeight && assistant.ActualHeight <= SystemParameters.WorkArea.Height,
            "长篇百科回答在窗口内滚动且适应屏幕高度");
        assistant.CancelSession(); assistant.Display(answer); assistant.CompleteQuery();
        check(!assistant.HasSession && !assistant.IsVisible, "关闭会清除提问会话，迟到回答不能重新打开窗口");
        config = config with { Assistant = config.Assistant with { PresetId = "ask" } };
        assistant.Present(selected); Control<Button>("SubmitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(submissions == 1 && !assistant.IsQuerying && Control<TextBlock>("QueryStatus").Text.Contains("请输入"), "自由提问需要填写问题，空白不会调用 API");
        assistant.CancelSession();
        config = config with { Assistant = config.Assistant with { PresetId = "encyclopedia" }, Overlay = config.Overlay with { ShowLoading = false } };
        assistant.Present(selected, autoRun: true); Pump(assistant);
        check(submissions == 2 && !assistant.IsVisible, "自动 AI 任务的仅结果模式在请求期间不显示窗口");
        assistant.ShowStatus("API 认证失败，请检查 Key 与账户权限。"); assistant.CompleteQuery(); Pump(assistant);
        check(assistant.IsVisible && Control<TextBlock>("QueryStatus").Text.Contains("认证失败"), "仅结果模式的认证错误也可见并允许重试");
        assistant.CancelSession();
        config = config with { Assistant = config.Assistant with { PresetId = "translate" } };
        var offlineSelection = selected with { Text = "米饭" };
        assistant.Present(offlineSelection); Control<Button>("SubmitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(submitted!.IsTranslation, "面板中的纯翻译任务保留专用离线请求格式");
        var translated = InputFixture.WaitWithDispatcher(vocabulary.FindAsync(submitted, default));
        check(translated?.Text == "ご飯", "新助手中的翻译仍由真实内置离线词库命中");
        assistant.Display(new(offlineSelection, submitted, translated!, 3, false, TranslationOrigin.Vocabulary)); Pump(assistant);
        capture(assistant, "assistant-offline-translation.png");
        assistant.CancelSession(); settingsWindow.Hide();
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout(); var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    private sealed class Protector : IDataProtector
    {
        public byte[] Protect(byte[] plain) => System.Security.Cryptography.ProtectedData.Protect(plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        public byte[] Unprotect(byte[] cipher) => System.Security.Cryptography.ProtectedData.Unprotect(cipher, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    }
}
