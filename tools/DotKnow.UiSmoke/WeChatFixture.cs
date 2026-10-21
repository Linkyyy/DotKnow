using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using DotKnow.App.Windows;
using DotKnow.Core;

namespace DotKnow.UiSmoke;

/// <summary>A separate Weixin-named test process exposes a copyable canvas without UIA text selection.</summary>
internal static class WeChatFixture
{
    internal static void Verify(Action<bool, string> check)
    {
        using var pointer = new PointerFixture();
        var host = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "Weixin.exe");
        File.Copy(Environment.ProcessPath!, host, false);
        try
        {
            var pipeName = "DotKnow-WeChat-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--input-fixture"); start.ArgumentList.Add(pipeName);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("微信兼容验收进程无法启动。");
            try
            {
                pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
                check(reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "ready", "微信式自绘控件验收进程就绪");
                void Command(string value)
                {
                    writer.WriteLine(value);
                    if (reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() != "ok") throw new InvalidOperationException("微信兼容验收进程未响应。");
                }
                var config = new AppSettings { PrivacyConsent = true };
                using var observer = new InputObserver(() => Volatile.Read(ref config));
                var original = new DataObject();
                original.SetData(DataFormats.UnicodeText, "original clipboard fixture");
                original.SetData(DataFormats.Html, "<b>original clipboard fixture</b>");
                Clipboard.SetDataObject(original, true);
                Command("custom-select");
                var selected = InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(default));
                check(selected is { Text: "微信自绘文字选区", IsSelection: true, CanWrite: false, EditRange: null },
                    "微信式无 TextPattern 自绘文本可通过快捷键捕获选区，且不具备替换权限");
                check(Clipboard.GetText() == "original clipboard fixture" && Clipboard.GetData(DataFormats.Html) as string == "<b>original clipboard fixture</b>",
                    "兼容读取后恢复原剪贴板文字和 HTML 格式");
                Command("custom-clear");
                check(InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(default)) == null && Clipboard.GetText() == "original clipboard fixture",
                    "复制没有产生新选区时不读取旧剪贴板，也不翻译原剪贴板内容");
                Command("custom-start"); pointer.Press(); Command("custom-end"); pointer.Release();
                async Task<InputSnapshot?> AfterRelease()
                {
                    for (var i = 0; i < 20; i++)
                    {
                        var snapshot = await observer.CaptureAsync(default);
                        if (snapshot != null) return snapshot;
                        await Task.Delay(100);
                    }
                    return null;
                }
                var automatic = InputFixture.WaitWithDispatcher(AfterRelease());
                check(automatic is { Text: "微信自绘文字选区", SelectionRevision: > 0 }, "微信式自绘文字完成鼠标划选后自动触发，无需快捷键");
                var gate = new SelectionDismissal(); gate.Dismiss(automatic);
                check(!gate.Allows(InputFixture.WaitWithDispatcher(observer.CaptureAsync(default))), "关闭微信选区结果后，轮询同一段文字不会重新打开");
                Volatile.Write(ref config, config with { Overlay = config.Overlay with { WeChatClipboardFallback = false } });
                check(InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(default)) == null, "关闭微信兼容选项后不进行临时复制");
                Volatile.Write(ref config, config with { Overlay = config.Overlay with { WeChatClipboardFallback = true }, ExcludedApplications = ["Weixin"] });
                check(InputFixture.WaitWithDispatcher(observer.RequestPreviewAsync(default)) == null && Clipboard.GetText() == "original clipboard fixture",
                    "微信兼容路径遵守应用排除项，不改写剪贴板");
                Command("stop"); process.WaitForExit(5000);
            }
            finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
        }
        finally { File.Delete(host); }
    }
}
