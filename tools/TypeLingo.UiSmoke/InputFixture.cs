using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        var panel = new StackPanel(); panel.Children.Add(input); panel.Children.Add(password); panel.Children.Add(rich);
        var window = new Window { Title = "伴译选区验收窗口", Width = 640, Height = 480, Content = panel, Topmost = true };
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
                    else if (command == "writable-selection") { input.IsReadOnly = false; input.Text = "prefix selected phrase suffix"; input.Focus(); input.Select(7, 15); }
                    else if (command == "multiline") { input.IsReadOnly = false; input.Text = "first line\r\ncurrent line\r\nlast line"; input.Focus(); input.Select(15, 0); }
                    else if (command == "value") { await writer.WriteLineAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input.Text))); continue; }
                    else if (command == "stop") { await writer.WriteLineAsync("ok"); app.Shutdown(); return; }
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
