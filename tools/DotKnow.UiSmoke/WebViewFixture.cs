using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DotKnow.App;
using DotKnow.App.Windows;
using DotKnow.Core;

namespace DotKnow.UiSmoke;

/// <summary>真实 WebView2 富文本与独立渲染进程，复现新版 Teams 的 UIA 输入结构；不登录 Teams 或调用 API。</summary>
internal static class WebViewFixture
{
    public static int Run(string pipeName)
    {
        var app = new Application();
        var profile = Path.Combine(Path.GetTempPath(), "DotKnow-WebView2-" + Guid.NewGuid().ToString("N"));
        var web = new WebView2();
        var window = new Window { Title = "DotKnow embedded editor fixture", Width = 700, Height = 460, Content = web, Topmost = true };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                await web.EnsureCoreWebView2Async(environment);
                var navigated = new TaskCompletionSource<bool>();
                web.CoreWebView2.NavigationCompleted += (_, e) => navigated.TrySetResult(e.IsSuccess);
                web.NavigateToString("""
                    <!doctype html><html><head><style>body{font:18px sans-serif;padding:20px}#compose{border:1px solid #888;padding:16px;min-height:120px;white-space:pre-wrap}input{margin-top:20px}</style></head>
                    <body><p id="reading">Selected webpage reading text.</p><button id="toolbar">Reading toolbar</button><div id="compose" role="textbox" aria-label="Message composer" aria-multiline="true" contenteditable="true"></div><input id="password" type="password" aria-label="Password" value="fixture-only"/>
                    <script>const editor=document.getElementById('compose');editor.textContent='first line\nTeams-style input\nlast line';
                    window.focusEditor=(start=14,end=start)=>{editor.focus();const range=document.createRange();range.setStart(editor.firstChild,start);range.setEnd(editor.firstChild,end);const s=window.getSelection();s.removeAllRanges();s.addRange(range);};
                    window.selectReading=()=>{document.getElementById('toolbar').focus();const r=document.createRange();r.selectNodeContents(document.getElementById('reading'));const s=window.getSelection();s.removeAllRanges();s.addRange(r);};</script></body></html>
                    """);
                if (!await navigated.Task.WaitAsync(TimeSpan.FromSeconds(20))) throw new InvalidOperationException("WebView2 navigation failed");
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(15000);
                using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
                async Task Focus(string script)
                {
                    window.Activate(); web.Focus(); await web.ExecuteScriptAsync(script);
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                }
                await Focus("focusEditor()"); await writer.WriteLineAsync("ready");
                while (await reader.ReadLineAsync() is { } command)
                {
                    if (command == "select") await Focus("focusEditor(11,28)");
                    else if (command == "edit") await Focus("editor.textContent='changed embedded input';focusEditor(22)");
                    else if (command == "password") await Focus("document.getElementById('password').focus()");
                    else if (command == "focus") await Focus("focusEditor()");
                    else if (command == "reading")
                    {
                        await Focus("selectReading()");
                        var rect = JsonDocument.Parse(await web.ExecuteScriptAsync("(()=>{const r=document.getElementById('reading').getBoundingClientRect();return {x:r.x+40,y:r.y+r.height/2}})()"));
                        using (rect) PointerFixture.Move(web.PointToScreen(new Point(rect.RootElement.GetProperty("x").GetDouble(), rect.RootElement.GetProperty("y").GetDouble())));
                    }
                    else if (command == "reading-clear") await Focus("window.getSelection().removeAllRanges();document.getElementById('toolbar').focus()");
                    else if (command == "reading-away") PointerFixture.Move(window.PointToScreen(new Point(window.ActualWidth - 12, window.ActualHeight - 45)));
                    else if (command == "stop") { await writer.WriteLineAsync("ok"); app.Shutdown(); return; }
                    await writer.WriteLineAsync("ok");
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); }
        };
        var exitCode = app.Run(window); web.Dispose();
        try { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return exitCode;
    }

    public static void Verify(Action<bool, string> check, Action<Window, string> capture, string output)
    {
        using var pointer = new PointerFixture();
        var pipeName = "DotKnow-WebView2-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("--webview-fixture"); start.ArgumentList.Add(pipeName);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start WebView2 fixture");
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        try
        {
            InputFixture.WaitWithDispatcher(pipe.WaitForConnectionAsync().ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }), 45);
            using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
            check(InputFixture.WaitWithDispatcher(reader.ReadLineAsync(), 30) == "ready", "真实 WebView2 富文本验收进程已启动");
            void Command(string value)
            {
                writer.WriteLine(value);
                if (InputFixture.WaitWithDispatcher(reader.ReadLineAsync()) != "ok") throw new InvalidOperationException("WebView2 fixture did not respond");
            }
            var config = new AppSettings { PrivacyConsent = true, MonitorAllApplications = false, AllowedApplications = [process.ProcessName],
                Overlay = new() { TriggerMode = "typing" },
                Translation = new() { DebounceMilliseconds = 100, MinimumRequestIntervalMilliseconds = 100 } };
            using var observer = new InputObserver(() => Volatile.Read(ref config));
            var automatic = new TaskCompletionSource<InputSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            observer.SnapshotChanged += value => { if (value?.Text == "Teams-style input") automatic.TrySetResult(value); };
            InputSnapshot? input;
            try { input = InputFixture.WaitWithDispatcher(automatic.Task); }
            catch
            {
                try { File.WriteAllText(Path.Combine(output, "webview2-uia-diagnostics.txt"), InputFixture.WaitWithDispatcher(Task.Run(Diagnostics), 5)); }
                catch (Exception diagnosticError) { File.WriteAllText(Path.Combine(output, "webview2-uia-diagnostics.txt"), diagnosticError.ToString()); }
                throw;
            }
            check(input.Text == "Teams-style input" && !input.IsSelection, "WebView2 未选中文本时自动捕获光标所在行，跳过聊天记录与其他行");
            var ids = input.Identity.Split(':');
            check(ids[0] != ids[1], "真实 WebView2 渲染进程与宿主 PID 不同，按前台窗口归属验证");
            check(!PrivacyGuard.Allows(config, "msedgewebview2", false, false), "只授权宿主，无需把所有 WebView2 应用加入允许列表");
            using var coordinator = new TranslationCoordinator(new FixtureProvider(), new EmptyCache(), () => config, new SessionStatistics());
            InputFixture.WaitWithDispatcher(coordinator.ObserveAsync(input).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            var overlay = new OverlayWindow(new ReplacementService(observer, coordinator), () => config);
            overlay.Display(coordinator.CurrentPreview);
            check(overlay.IsVisible && ((TextBlock)overlay.FindName("TranslationText")).Text == "嵌入输入框测试译文", "真实 WebView2 输入可进入翻译预览并显示悬浮窗");
            capture(overlay, "webview2-preview.png"); overlay.Dismiss();
            Volatile.Write(ref config, config with { Overlay = config.Overlay with { TriggerMode = "selection" } });
            Command("focus");
            check(InputFixture.WaitWithDispatcher(observer.CaptureAsync(CancellationToken.None)) == null, "默认划词模式不自动翻译 WebView2 输入行");
            var selectedReading = new TaskCompletionSource<InputSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            observer.SnapshotChanged += value => { if (value?.Text == "Selected webpage reading text.") selectedReading.TrySetResult(value); };
            Command("reading");
            var reading = InputFixture.WaitWithDispatcher(selectedReading.Task);
            check(reading is { IsSelection: true, CanWrite: false, EditRange: null }, "网页阅读正文选区在工具栏持有键盘焦点时仍自动触发，且不允许写入");
            using var readingCoordinator = new TranslationCoordinator(new FixtureProvider(), new EmptyCache(), () => config, new SessionStatistics());
            InputFixture.WaitWithDispatcher(readingCoordinator.ObserveAsync(reading).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            var readingOverlay = new OverlayWindow(new ReplacementService(observer, readingCoordinator), () => config);
            readingOverlay.Display(readingCoordinator.CurrentPreview);
            check(readingOverlay.IsVisible && ((System.Windows.Controls.Button)readingOverlay.FindName("ReplaceButton")).Visibility == Visibility.Collapsed,
                "网页选区实际显示译文并隐藏替换按钮");
            capture(readingOverlay, "webview2-reading-selection.png"); readingOverlay.Dismiss();
            Command("reading-away");
            check(InputFixture.WaitWithDispatcher(observer.CaptureAsync(CancellationToken.None))?.SameContent(reading) == true, "鼠标离开网页选中文字后，阅读预览仍可使用");
            Command("reading-clear");
            async Task<bool> SelectionClears()
            {
                for (var attempt = 0; attempt < 30; attempt++)
                { if (await observer.CaptureAsync(CancellationToken.None) == null) return true; await Task.Delay(100); }
                return false;
            }
            check(InputFixture.WaitWithDispatcher(SelectionClears()), "取消网页选区后不翻译整页或工具栏");
            Command("focus");
            Volatile.Write(ref config, config with { Overlay = config.Overlay with { TriggerMode = "hotkey" } });
            check(InputFixture.WaitWithDispatcher(observer.CaptureAsync(CancellationToken.None)) == null, "WebView2 快捷键模式在触发前不读取");
            check(InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(CancellationToken.None))?.Text == "Teams-style input", "WebView2 快捷键触发能读取当前行");
            Command("edit");
            async Task<bool> BecomesUnavailable(bool manual)
            {
                for (var n = 0; n < 30; n++)
                {
                    var value = await (manual ? observer.RequestPreviewAsync(CancellationToken.None) : observer.CaptureAsync(CancellationToken.None));
                    if (value == null) return true;
                    await Task.Delay(100);
                }
                return false;
            }
            check(InputFixture.WaitWithDispatcher(BecomesUnavailable(false)), "WebView2 编辑后旧快捷键授权失效");
            Volatile.Write(ref config, config with { Overlay = config.Overlay with { TriggerMode = "typing" }, ExcludedApplications = ["msedgewebview2"] });
            check(InputFixture.WaitWithDispatcher(observer.CaptureAsync(CancellationToken.None)) == null, "WebView2 渲染进程排除项仍优先");
            Volatile.Write(ref config, config with { ExcludedApplications = [] });
            Command("password");
            check(InputFixture.WaitWithDispatcher(BecomesUnavailable(true)), "WebView2 真实密码框继续跳过");
            Command("stop"); process.WaitForExit(5000);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            process.WaitForExit(5000);
            lock (errors) if (errors.Length > 0) File.WriteAllText(Path.Combine(output, "webview2-fixture-stderr.txt"), errors.ToString());
        }
    }

    private static string Diagnostics()
    {
        var lines = new List<string>(); var node = AutomationElement.FocusedElement;
        for (var i = 0; i < 16 && node != null; i++, node = TreeWalker.RawViewWalker.GetParent(node))
        {
            var state = node.Current; object? readOnly = null;
            var text = node.TryGetCurrentPattern(TextPattern.Pattern, out var pattern);
            if (text) readOnly = ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute)?.ToString();
            lines.Add(JsonSerializer.Serialize(new { state.ProcessId, state.NativeWindowHandle, Type = state.ControlType.ProgrammaticName,
                state.HasKeyboardFocus, state.IsPassword, state.IsOffscreen, TextPattern = text, ReadOnly = readOnly,
                ValuePattern = node.TryGetCurrentPattern(ValuePattern.Pattern, out _) }));
        }
        return string.Join('\n', lines);
    }
    private sealed class FixtureProvider : ITranslationProvider
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            var text = request.Text switch { "Teams-style input" => "嵌入输入框测试译文", "Selected webpage reading text." => "选中的网页阅读文本。", _ => throw new InvalidOperationException("Unexpected text outside fixture selection") };
            return Task.FromResult(new TranslationResult(text));
        }
    }
    private sealed class EmptyCache : ITranslationCache
    {
        public Task<TranslationResult?> GetAsync(string key, CancellationToken token) => Task.FromResult<TranslationResult?>(null);
        public Task PutAsync(string key, TranslationResult value, CancellationToken token) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
