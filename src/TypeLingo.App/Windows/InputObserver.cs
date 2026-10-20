using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using TypeLingo.Core;

namespace TypeLingo.App.Windows;

/// <summary>全部 UIA 调用和事件订阅均在专用后台 MTA 线程执行，不占用 WPF 界面线程。</summary>
internal sealed class InputObserver : IInputTarget, IDisposable
{
    private readonly Func<AppSettings> settings;
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;
    private volatile bool stopping;
    private AutomationElement? subscribed;
    private bool focusSubscribed;
    private readonly AutomationFocusChangedEventHandler focusChanged;
    private readonly AutomationEventHandler textChanged;
    private int dirty = 1;
    private readonly PreviewActivation activation = new();
    private AppSettings? capturedSettings;
    private AutomationElement? capturedElement;
    public event Action<InputSnapshot?>? SnapshotChanged;
    public event Action<string>? StatusChanged;
    public InputObserver(Func<AppSettings> settings)
    {
        this.settings = settings;
        focusChanged = (_, _) => Interlocked.Exchange(ref dirty, 1);
        textChanged = (_, _) => Interlocked.Exchange(ref dirty, 1);
        thread = new Thread(Run) { IsBackground = true, Name = "TypeLingo UI Automation" };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }

    private void Run()
    {
        var lastPoll = Environment.TickCount64;
        try
        {
            while (!stopping)
            {
                if (work.TryTake(out var action, 100)) action();
                // 合并短时间内的事件；对于遗漏事件的控件提供程序，使用较慢的轮询兜底。
                if (Environment.TickCount64 - lastPoll < (Volatile.Read(ref dirty) != 0 ? 100 : 300)) continue;
                lastPoll = Environment.TickCount64;
                var config = settings();
                if (!config.PrivacyConsent || !config.Translation.Enabled)
                { activation.Reset(); Unsubscribe(); SnapshotChanged?.Invoke(null); continue; }
                try
                {
                    if (!focusSubscribed) { Automation.AddAutomationFocusChangedEventHandler(focusChanged); focusSubscribed = true; }
                    // 每 300 毫秒兜底检查没有事件通知的文本、位置、焦点和输入法变化。
                    Interlocked.Exchange(ref dirty, 0);
                    SnapshotChanged?.Invoke(Capture());
                }
                catch (Exception e) when (IsUiaFailure(e))
                { activation.Reset(); SnapshotChanged?.Invoke(null); StatusChanged?.Invoke("当前控件不支持安全读取，请切换输入框。"); }
                catch (Exception)
                { activation.Reset(); SnapshotChanged?.Invoke(null); StatusChanged?.Invoke("输入监视暂时不可用；未执行写入，请暂停或切换应用。"); }
            }
        }
        finally { Unsubscribe(); }
    }

    private InputSnapshot? Capture(bool explicitRequest = false)
    {
        var config = settings();
        if (!ReferenceEquals(capturedSettings, config)) { activation.Reset(); capturedSettings = config; }
        if (!config.PrivacyConsent || !config.Translation.Enabled) { activation.Reset(); return null; }
        if (!explicitRequest && !activation.ShouldCapture(config.Overlay.TriggerMode)) { SubscribeText(null); return null; }
        var content = CaptureContent(config);
        if (!ReferenceEquals(config, settings())) { activation.Reset(); return null; }
        return activation.Filter(content, config.Overlay.TriggerMode, explicitRequest);
    }

    private InputSnapshot? CaptureContent(AppSettings config)
    {
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) { SubscribeText(null); return null; }
        string process;
        using (var p = Process.GetProcessById((int)pid)) process = p.ProcessName;
        // 先检查应用授权，再访问控件。快捷键模式在触发前完全不读取文本。
        if (!PrivacyGuard.Allows(config, process, false, false)) { SubscribeText(null); return null; }
        var focused = AutomationElement.FocusedElement;
        if (focused == null) { SubscribeText(null); return null; }
        var state = focused.Current;
        if (state.ProcessId != pid || !state.IsEnabled || state.IsOffscreen || state.IsPassword || Sensitive(focused))
        { SubscribeText(null); return null; }
        if (Native.IsComposing(foreground)) return new InputSnapshot("composition", "", new(0, 0, 0, 0), false, true);
        const int captureLimit = 200000;
        var element = focused;
        var controlBounds = Rect(state.BoundingRectangle);
        var bounds = controlBounds;
        string? text = null;
        InputTextRange? editRange = null;
        var selection = false;
        var canWrite = false;
        // 所有触发方式都优先使用实际选区；不通过剪贴板推测选区位置。
        var candidate = focused;
        for (var level = 0; level < 5 && candidate != null; level++)
        {
            var candidateState = candidate.Current;
            if (candidateState.ProcessId != pid) break;
            if (candidateState.IsPassword || Sensitive(candidate)) return null;
            if (candidate.TryGetCurrentPattern(TextPattern.Pattern, out var textObject))
            {
                var pattern = (TextPattern)textObject;
                var ranges = pattern.GetSelection();
                if (ranges.Length > 1) { SubscribeText(null); return null; }
                if (ranges.Length == 1)
                {
                    var range = ranges[0];
                    var selected = range.GetText(config.Translation.MaxInputCharacters + 1);
                    selection = range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0;
                    if (selection || candidate == focused)
                    {
                        element = candidate;
                        controlBounds = Rect(candidateState.BoundingRectangle);
                        bounds = RangeBounds(range, controlBounds);
                        if (selection) text = selected;
                        if (candidate.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject))
                        {
                            var value = (ValuePattern)valueObject;
                            var full = value.Current.Value;
                            if (full.Length <= captureLimit)
                            {
                                var prefix = pattern.DocumentRange.Clone();
                                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                                var mapped = InputTextRange.FromAutomation(full, pattern.DocumentRange.GetText(captureLimit + 1),
                                    prefix.GetText(captureLimit + 1), selection ? selected : "");
                                if (mapped != null)
                                {
                                    editRange = selection ? mapped : InputTextRange.LineAt(full, mapped.Start);
                                    text = editRange.Text;
                                    canWrite = !value.Current.IsReadOnly &&
                                        Native.CanVerifyCompositionForWrite(foreground, candidateState.ClassName, candidateState.NativeWindowHandle);
                                }
                            }
                        }
                        // 无可靠 ValuePattern/偏移时，选区仍可预览；不猜测写入范围。
                        if (text != null) break;
                    }
                }
            }
            candidate = TreeWalker.ControlViewWalker.GetParent(candidate);
        }
        if (config.Overlay.TriggerMode == "selection" && !selection) { SubscribeText(focused); return null; }
        if (text == null)
        {
            if (!state.HasKeyboardFocus || (state.ControlType != ControlType.Edit && state.ControlType != ControlType.Document))
            { SubscribeText(null); return null; }
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject))
            {
                var value = (ValuePattern)valueObject;
                var full = value.Current.Value;
                // 无 TextPattern 的单行编辑框可安全处理；多行框必须能取得光标位置。
                if (!full.Contains('\r') && !full.Contains('\n'))
                {
                    element = focused; editRange = new(full, 0, full.Length); text = full;
                    canWrite = !value.Current.IsReadOnly &&
                        Native.CanVerifyCompositionForWrite(foreground, state.ClassName, state.NativeWindowHandle);
                }
            }
        }
        if (text == null || text.Length > config.Translation.MaxInputCharacters) { SubscribeText(null); return null; }
        if (Native.GetForegroundWindow() != foreground || !Automation.Compare(focused, AutomationElement.FocusedElement)) return null;
        capturedElement = element;
        SubscribeText(element);
        return new InputSnapshot($"{pid}:{foreground}:{string.Join('.', element.GetRuntimeId())}",
            text, bounds, canWrite, IsSelection: selection, ControlBounds: controlBounds, EditRange: editRange);
    }
    private static bool Sensitive(AutomationElement element)
    {
        var metadata = element.Current.AutomationId + " " + element.Current.Name;
        return new[] { "password", "passwd", "secret", "api key", "apikey", "token", "密钥", "credit card", "creditcard", "cvv", "one-time", "otp", "密码", "密碼", "パスワード" }
            .Any(x => metadata.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
    private static InputRect Rect(System.Windows.Rect value) => value.IsEmpty ? new(0, 0, 0, 0) : new(value.X, value.Y, value.Width, value.Height);
    private static InputRect RangeBounds(TextPatternRange range, InputRect fallback)
    {
        var rectangles = range.GetBoundingRectangles();
        return rectangles.Length > 0 && !rectangles[0].IsEmpty && rectangles[0].Width > 0 && rectangles[0].Height > 0 ? Rect(rectangles[0]) : fallback;
    }

    private void SubscribeText(AutomationElement? element)
    {
        if (subscribed != null && element != null && Automation.Compare(subscribed, element)) return;
        if (subscribed != null)
        {
            try { foreach (var eventId in new[] { TextPattern.TextChangedEvent, TextPattern.TextSelectionChangedEvent })
                Automation.RemoveAutomationEventHandler(eventId, subscribed, textChanged); }
            catch (Exception e) when (IsUiaFailure(e)) { }
        }
        subscribed = null;
        if (element == null) return;
        try { subscribed = element;
            foreach (var eventId in new[] { TextPattern.TextChangedEvent, TextPattern.TextSelectionChangedEvent })
                Automation.AddAutomationEventHandler(eventId, element, TreeScope.Element, textChanged); }
        catch (Exception e) when (IsUiaFailure(e)) { }
    }
    private void Unsubscribe()
    {
        SubscribeText(null);
        if (!focusSubscribed) return;
        try { Automation.RemoveAutomationFocusChangedEventHandler(focusChanged); }
        catch (Exception e) when (IsUiaFailure(e)) { }
        focusSubscribed = false;
    }
    public Task<InputSnapshot?> CaptureAsync(CancellationToken cancellationToken) => Enqueue(() => Capture(), cancellationToken);
    public Task<InputSnapshot?> RequestPreviewAsync(CancellationToken cancellationToken) => Enqueue(() => Capture(true), cancellationToken);
    public Task<bool> TryReplaceAsync(InputSnapshot expected, string translation, CancellationToken cancellationToken) => Enqueue(() =>
    {
        var now = Capture();
        if (now is not { CanWrite: true, IsComposing: false, IsSensitive: false } || !expected.SameContent(now) ||
            expected.EditRange is not { IsValid: true } range || range.Text != expected.Text) return false;
        var element = capturedElement;
        var focused = AutomationElement.FocusedElement;
        if (element == null || focused == null || !element.TryGetCurrentPattern(ValuePattern.Pattern, out var obj)) return false;
        var value = (ValuePattern)obj;
        // 紧邻 SetValue 再次复核全文、范围和焦点；仅替换选区或当前行，保留其余内容。
        var final = Capture();
        if (final is not { CanWrite: true, IsComposing: false, IsSensitive: false } || !expected.SameContent(final) ||
            !Automation.Compare(element, capturedElement) || !Automation.Compare(focused, AutomationElement.FocusedElement) ||
            value.Current.IsReadOnly || value.Current.Value != range.FullText) return false;
        cancellationToken.ThrowIfCancellationRequested();
        value.SetValue(range.Replace(translation));
        return true;
    }, cancellationToken);

    private Task<T> Enqueue<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stopping) { completion.SetException(new ObjectDisposedException(nameof(InputObserver))); return completion.Task; }
        work.Add(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception e) when (IsUiaFailure(e)) { completion.TrySetResult(default!); }
            catch (Exception e) { completion.TrySetException(e); }
        });
        return completion.Task;
    }
    private static bool IsUiaFailure(Exception e) => e is ElementNotAvailableException or InvalidOperationException or NotSupportedException or
        System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception;
    public void Dispose() { stopping = true; /* 第三方 UIA 提供程序可能挂起后台调用；退出时不等待线程而阻塞程序。 */ }
}
