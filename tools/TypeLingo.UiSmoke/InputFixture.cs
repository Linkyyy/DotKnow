using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using TypeLingo.App.Windows;
using TypeLingo.Core;

namespace TypeLingo.UiSmoke;

/// <summary>独立测试进程提供真实 WPF 输入框，验证跨进程 UIA，不启动付费 API。</summary>
internal static class InputFixture
{
    public static int Run(string pipeName)
    {
        var app = new Application();
        var input = new TextBox { Text = "prefix selected phrase suffix", AcceptsReturn = true, Height = 110, Margin = new Thickness(16) };
        var password = new PasswordBox { Password = "fixture-only-password", Margin = new Thickness(16) };
        var rich = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("rich editor current line"))), Height = 90, Margin = new Thickness(16) };
        var readingRun = new Run("selected reading phrase");
        var reading = new FlowDocumentScrollViewer { Document = new FlowDocument(new Paragraph(readingRun)) { PagePadding = new Thickness(8), FontSize = 18 },
            Height = 100, Margin = new Thickness(16), IsToolBarVisible = false, Focusable = false };
        var panel = new StackPanel(); panel.Children.Add(input); panel.Children.Add(password); panel.Children.Add(rich); panel.Children.Add(reading);
        var window = new Window { Title = "伴译选区验收窗口", Width = 680, Height = 620, Content = panel, Topmost = true };
        var mouseEvents = new List<string>();
        TaskCompletionSource<bool>? dragMoved = null;
        reading.PreviewMouseDown += (_, e) => mouseEvents.Add("down:" + e.GetPosition(reading));
        reading.PreviewMouseMove += (_, e) => {
            if (e.LeftButton == MouseButtonState.Pressed)
            { mouseEvents.Add("move:" + e.GetPosition(reading)); if (e.GetPosition(reading).X > 100) dragMoved?.TrySetResult(true); }
        };
        reading.PreviewMouseUp += (_, e) => mouseEvents.Add("up:" + e.GetPosition(reading));
        FrameworkElement ReadingScope() => (reading.Template.FindName("PART_ContentHost", reading) as ScrollViewer)?.Content as FrameworkElement ?? reading;
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(10000);
                using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
                input.Focus(); input.Select(0, 0); await writer.WriteLineAsync("ready");
                while (await reader.ReadLineAsync() is { } command)
                {
                    window.Activate();
                    if (command == "edit") { input.IsReadOnly = false; input.Text = "edited input"; input.Focus(); input.Select(0, 0); }
                    else if (command == "select") { input.Text = "prefix selected phrase suffix"; input.IsReadOnly = true; input.Focus(); input.Select(7, 15); }
                    else if (command == "clear") { input.Focus(); input.Select(0, 0); }
                    else if (command == "password") password.Focus();
                    else if (command == "rich") { rich.Focus(); rich.CaretPosition = rich.Document.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!; }
                    else if (command is "reading" or "reading-conflict")
                    {
                        if (command == "reading-conflict") { input.Text = "short"; input.SelectAll(); }
                        reading.Focusable = false;
                        reading.Selection.Select(readingRun.ContentStart, readingRun.ContentEnd); input.Focus();
                        var rect = readingRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        PointerFixture.Move(ReadingScope().PointToScreen(new Point(rect.Left + 20, rect.Top + rect.Height / 2)));
                    }
                    else if (command == "reading-clear") { reading.Selection.Select(readingRun.ContentStart, readingRun.ContentStart); input.Focus(); }
                    else if (command == "reading-away") PointerFixture.Move(window.PointToScreen(new Point(window.ActualWidth - 12, window.ActualHeight - 45)));
                    else if (command == "pointer-settle") await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    else if (command == "reading-start" || command == "reading-end")
                    {
                        // WPF 自身要求阅读器可聚焦才能处理真实鼠标拖选；上面的用例单独验证无键盘焦点的读取。
                        reading.Focusable = true;
                        if (command == "reading-end") dragMoved = new TaskCompletionSource<bool>();
                        var rect = command == "reading-start" ? readingRun.ContentStart.GetCharacterRect(LogicalDirection.Forward) : readingRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                        PointerFixture.Move(ReadingScope().PointToScreen(new Point(command == "reading-start" ? rect.Left + 1 : rect.Right + 2, rect.Top + rect.Height / 2)));
                        if (command == "reading-end") await dragMoved!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    else if (command == "reading-state")
                    {
                        await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new {
                            Text = reading.Selection.Text, Focus = Keyboard.FocusedElement?.GetType().Name, reading.IsKeyboardFocusWithin,
                            reading.Focusable, window.IsActive, LeftButton = Mouse.LeftButton.ToString(), Events = mouseEvents,
                            Start = readingRun.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(),
                            End = readingRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward).ToString(),
                            Scope = ReadingScope().GetType().Name }));
                        continue;
                    }
                    else if (command == "writable-selection") { input.IsReadOnly = false; input.Text = "prefix selected phrase suffix"; input.Focus(); input.Select(7, 15); }
                    else if (command == "multiline") { input.IsReadOnly = false; input.Text = "first line\r\ncurrent line\r\nlast line"; input.Focus(); input.Select(15, 0); }
                    else if (command == "value") { await writer.WriteLineAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input.Text))); continue; }
                    else if (command == "stop") { await writer.WriteLineAsync("ok"); app.Shutdown(); return; }
                    if (command is "edit" or "select" or "clear" or "writable-selection" or "multiline")
                        PointerFixture.Move(input.PointToScreen(new Point(30, 20)));
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    await writer.WriteLineAsync("ok");
                }
            }
            catch (Exception) { app.Shutdown(1); }
        };
        return app.Run(window);
    }

    public static void Verify(Action<bool, string> check)
    {
        using var pointer = new PointerFixture();
        var pipeName = "TypeLingo-UIA-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--input-fixture"); start.ArgumentList.Add(pipeName);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动输入验收进程");
        try
        {
            pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
            check(reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "ready", "独立 WPF 输入进程已连接，使用真实跨进程 UIA");
            void Command(string value)
            {
                writer.WriteLine(value);
                if (reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() != "ok") throw new InvalidOperationException("验收窗口未响应");
            }
            var config = new AppSettings { PrivacyConsent = true, Overlay = new() { TriggerMode = "hotkey" } };
            using var observer = new InputObserver(() => Volatile.Read(ref config));
            InputSnapshot? Capture(bool manual = false) => WaitWithDispatcher(manual ? observer.RequestPreviewAsync(CancellationToken.None) : observer.CaptureAsync(CancellationToken.None));
            check(Capture() == null, "快捷键模式在触发前不读取输入文本");
            var manual = Capture(true);
            check(manual?.Text == "prefix selected phrase suffix" && !manual.IsSelection, "快捷键可读取当前真实输入框");
            Command("edit");
            check(Capture() == null, "编辑输入后旧的快捷键预览立即失效");
            check(Capture(true)?.Text == "edited input", "再次按快捷键才能取得修改后的输入");
            Volatile.Write(ref config, config with { Overlay = config.Overlay with { TriggerMode = "selection" } });
            Command("select"); var selected = Capture();
            check(selected?.Text == "selected phrase" && selected.IsSelection && !selected.CanWrite, "只读输入框的真实选区可翻译，且不会替换正文");
            Command("clear"); check(Capture() == null, "清除选区后不读取或翻译整个文档");
            Command("reading"); var reading = Capture();
            check(reading?.Text == "selected reading phrase" && reading.IsSelection && !reading.CanWrite && reading.EditRange == null,
                "非输入框阅读选区可从鼠标位置读取，不依赖键盘焦点，也不读取整篇正文");
            Command("reading-away");
            check(Capture()?.SameContent(reading) == true, "鼠标离开阅读文字后仍复核原选区，允许操作悬浮窗");
            Command("reading-clear"); check(Capture() == null, "清除阅读选区后立即停止预览，不回退到全文或输入行");
            Command("reading-start"); pointer.Press(); Command("pointer-settle"); Command("reading-end");
            check(Capture() == null, "鼠标按住拖选时不发送中间选区"); pointer.Release();
            async Task<InputSnapshot?> AfterRelease()
            {
                InputSnapshot? last = null;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    last = await observer.CaptureAsync(CancellationToken.None);
                    if (last?.Text.TrimEnd('\r', '\n') == "selected reading phrase") return last;
                    await Task.Delay(100);
                }
                return last;
            }
            var dragged = WaitWithDispatcher(AfterRelease());
            writer.WriteLine("reading-state");
            var dragState = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            check(dragged is { IsSelection: true, CanWrite: false } && dragged.Text.TrimEnd('\r', '\n') == "selected reading phrase",
                "真实鼠标拖选并松开后捕获只读阅读区的完整选中文字：" + System.Text.Json.JsonSerializer.Serialize(dragged) + "; fixture=" + dragState);
            Volatile.Write(ref config, config with { ExcludedApplications = [process.ProcessName] });
            check(Capture() == null, "阅读选区继续遵守进程排除项");
            Volatile.Write(ref config, config with { ExcludedApplications = [] });
            Volatile.Write(ref config, config with { Translation = config.Translation with { MaxInputCharacters = 10 } });
            Command("reading-conflict");
            check(Capture() == null, "鼠标选区超过长度限制时跳过，不回退翻译其他输入框的旧选区");
            Volatile.Write(ref config, config with { Translation = config.Translation with { MaxInputCharacters = 2000 } });
            Command("writable-selection"); selected = Capture();
            check(selected is { IsSelection: true, CanWrite: true } && selected.EditRange?.Start == 7, "可编辑选区具有精确写入范围");
            check(WaitWithDispatcher(observer.TryReplaceAsync(selected!, "译文", CancellationToken.None)), "真实 UIA 可替换当前选区");
            string ReadValue()
            {
                writer.WriteLine("value");
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!));
            }
            check(ReadValue() == "prefix 译文 suffix", "选区替换保留前缀和后缀");
            Volatile.Write(ref config, config with { Overlay = config.Overlay with { TriggerMode = "typing" } });
            Command("multiline"); var line = Capture();
            check(line?.Text == "current line" && line.EditRange?.Start == 12 && !line.IsSelection, "非选区仅捕获光标所在逻辑行");
            check(WaitWithDispatcher(observer.TryReplaceAsync(line!, "当前行译文", CancellationToken.None)), "真实 UIA 可替换当前行");
            check(ReadValue() == "first line\r\n当前行译文\r\nlast line", "当前行替换保留其他行和 CRLF");
            Command("multiline"); line = Capture(); Command("edit");
            check(!WaitWithDispatcher(observer.TryReplaceAsync(line!, "不应写入", CancellationToken.None)), "确认期间内容变化时拒绝旧范围写入");
            Command("rich"); var rich = Capture();
            check(rich?.Text == "rich editor current line" && !rich.CanWrite && !rich.IsSelection,
                "仅提供 TextPattern 的真实富文本控件，无选区也能自动预览当前行");
            Command("password"); check(Capture(true) == null, "三种触发方式继续跳过真实密码框");
            Command("stop"); process.WaitForExit(5000);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    internal static T WaitWithDispatcher<T>(Task<T> operation, int timeoutSeconds = 10)
    {
        // 保持客户端 WPF 调度器运行，避免 UIA 在跨进程回调时等待已阻塞的界面线程。
        var bounded = operation.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = bounded.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        Dispatcher.PushFrame(frame); return bounded.GetAwaiter().GetResult();
    }
}
