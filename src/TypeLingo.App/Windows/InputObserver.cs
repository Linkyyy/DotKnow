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
    private AutomationElement? selectionTarget;
    private AutomationElement? selectionFocus;
    private nint selectionWindow;
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
                { activation.Reset(); ClearSelectionTarget(); Unsubscribe(); SnapshotChanged?.Invoke(null); continue; }
                try
                {
                    if (!focusSubscribed) { Automation.AddAutomationFocusChangedEventHandler(focusChanged); focusSubscribed = true; }
                    // 每 300 毫秒兜底检查没有事件通知的文本、位置、焦点和输入法变化。
                    Interlocked.Exchange(ref dirty, 0);
                    SnapshotChanged?.Invoke(Capture());
                }
                catch (Exception e) when (IsUiaFailure(e))
                { activation.Reset(); ClearSelectionTarget(); SnapshotChanged?.Invoke(null); StatusChanged?.Invoke("当前控件不支持安全读取选区，请尝试其他文本。"); }
                catch (Exception)
                { activation.Reset(); ClearSelectionTarget(); SnapshotChanged?.Invoke(null); StatusChanged?.Invoke("输入监视暂时不可用；未执行写入，请暂停或切换应用。"); }
            }
        }
        finally { Unsubscribe(); }
    }

    private InputSnapshot? Capture(bool explicitRequest = false)
    {
        var config = settings();
        if (!ReferenceEquals(capturedSettings, config)) { activation.Reset(); ClearSelectionTarget(); capturedSettings = config; }
        if (!config.PrivacyConsent || !config.Translation.Enabled) { activation.Reset(); ClearSelectionTarget(); return null; }
        if (!explicitRequest && !activation.ShouldCapture(config.Overlay.TriggerMode)) { SubscribeText(null); return null; }
        var content = CaptureContent(config);
        if (!ReferenceEquals(config, settings())) { activation.Reset(); return null; }
        return activation.Filter(content, config.Overlay.TriggerMode, explicitRequest);
    }

    private InputSnapshot? CaptureContent(AppSettings config)
    {
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) { ClearSelectionTarget(); SubscribeText(null); return null; }
        string process;
        using (var p = Process.GetProcessById((int)pid)) process = p.ProcessName;
        // 先检查前台宿主授权，再访问 UIA；快捷键模式在触发前完全不读取文本。
        if (!PrivacyGuard.Allows(config, process, false, false)) { ClearSelectionTarget(); SubscribeText(null); return null; }
        var focused = AutomationElement.FocusedElement;
        if (focused is not null && (focused.Current.IsPassword || Sensitive(focused)))
        { ClearSelectionTarget(); SubscribeText(null); return null; }
        if (selectionWindow != foreground || !SameElement(selectionFocus, focused)) ClearSelectionTarget();
        if (Native.IsComposing(foreground)) return new InputSnapshot("composition", "", new(0, 0, 0, 0), false, true);

        var pointAvailable = Native.GetCursorPos(out var point);
        var pointerIsOurs = false;
        if (pointAvailable)
        {
            Native.GetWindowThreadProcessId(Native.WindowFromPoint(point), out var pointerPid);
            pointerIsOurs = pointerPid == Environment.ProcessId;
        }
        // 拖选过程中清除旧预览、取消旧请求；点击不激活的自有悬浮窗时保留选区。
        if (Native.IsSelecting && !pointerIsOurs) { ClearSelectionTarget(); return null; }
        var focusedPath = focused == null ? null : ForegroundPath(focused, foreground);
        var processNames = new Dictionary<int, string> { [(int)pid] = process };
        InputSnapshot? result = null;
        var selectionFirst = config.Overlay.TriggerMode != "typing";
        if (!selectionFirst && focusedPath != null)
            result = ReadPath(config, focusedPath, focusedPath, foreground, pid, process, processNames, false);
        if (result == null && pointAvailable && !pointerIsOurs)
        {
            // 阅读文档的键盘焦点可能仍在工具栏：从鼠标命中的文本向上查找实际选区。
            var pointed = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            var pointedPath = pointed == null ? null : ForegroundPath(pointed, foreground);
            if (pointedPath != null)
                result = ReadPath(config, pointedPath, focusedPath, foreground, pid, process, processNames, true);
        }
        if (result == null && selectionTarget != null)
        {
            // 鼠标移到译文的复制／收藏按钮后继续复核原选区，不要求它获取键盘焦点。
            var savedPath = ForegroundPath(selectionTarget, foreground);
            if (savedPath != null)
                result = ReadPath(config, savedPath, focusedPath, foreground, pid, process, processNames, true);
        }
        if (result == null && focusedPath != null)
            result = ReadPath(config, focusedPath, focusedPath, foreground, pid, process, processNames,
                config.Overlay.TriggerMode == "selection");
        if (result == null)
        { ClearSelectionTarget(); SubscribeText(focused); return null; }
        if (Native.GetForegroundWindow() != foreground || !SameElement(focused, AutomationElement.FocusedElement)) return null;
        if (Native.IsSelecting && !pointerIsOurs) return null;
        if (result.IsSelection)
        { selectionTarget = capturedElement; selectionFocus = focused; selectionWindow = foreground; }
        else ClearSelectionTarget();
        SubscribeText(capturedElement);
        return result;
    }

    private InputSnapshot? ReadPath(AppSettings config, List<AutomationElement> path, List<AutomationElement>? focusedPath,
        nint foreground, uint pid, string process, Dictionary<int, string> processNames, bool selectionOnly)
    {
        // 渲染进程可不同于宿主，但整条路径必须实际位于当前前台 HWND 下，且未被排除。
        foreach (var node in path)
        {
            var state = node.Current;
            if (state.ProcessId <= 0 || state.ProcessId == Environment.ProcessId || state.IsPassword || Sensitive(node)) return null;
            if (!processNames.TryGetValue(state.ProcessId, out var name))
            {
                using var childProcess = Process.GetProcessById(state.ProcessId);
                processNames[state.ProcessId] = name = childProcess.ProcessName;
            }
            if (!PrivacyGuard.AllowsHostedInput(config, process, name, true)) return null;
        }
        const int captureLimit = 200000;
        foreach (var candidate in path)
        {
            var state = candidate.Current;
            if (!state.IsEnabled || state.IsOffscreen) continue;
            var controlBounds = Rect(state.BoundingRectangle);
            var bounds = controlBounds;
            string? text = null;
            InputTextRange? editRange = null;
            var selection = false;
            var canWrite = false;
            ValuePattern? value = candidate.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) ? (ValuePattern)valueObject : null;
            var focusedTarget = focusedPath?.Any(node => Automation.Compare(node, candidate)) == true;
            if (candidate.TryGetCurrentPattern(TextPattern.Pattern, out var textObject))
            {
                var pattern = (TextPattern)textObject;
                var ranges = pattern.GetSelection();
                if (ranges.Length > 1) return null;
                if (ranges.Length != 1) continue;
                var range = ranges[0];
                selection = range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0;
                if (selectionOnly && !selection) continue;
                var editable = value?.Current.IsReadOnly == false ||
                    pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
                if (!selection && !editable) continue;
                var anchorRange = range.Clone();
                if (anchorRange.GetBoundingRectangles().Length == 0) anchorRange.ExpandToEnclosingUnit(TextUnit.Character);
                bounds = RangeBounds(anchorRange, controlBounds);
                var selected = selection ? range.GetText(config.Translation.MaxInputCharacters + 1) : "";
                if (selection) text = selected;
                // 阅读选区只读取所选文字；仅在当前编辑目标需要核对写入范围时读取全文。
                if ((focusedTarget && value?.Current.IsReadOnly == false) || (!selection && editable))
                {
                    var prefix = pattern.DocumentRange.Clone();
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                    var document = pattern.DocumentRange.GetText(captureLimit + 1);
                    var before = prefix.GetText(captureLimit + 1);
                    if (focusedTarget && value != null)
                    {
                        var full = value.Current.Value;
                        if (full.Length <= captureLimit && document.Length <= captureLimit && before.Length <= captureLimit)
                        {
                            var mapped = InputTextRange.FromAutomation(full, document, before, selected);
                            if (mapped != null)
                            {
                                editRange = selection ? mapped : InputTextRange.LineAt(full, mapped.Start);
                                text = editRange.Text;
                                canWrite = !value.Current.IsReadOnly &&
                                    Native.CanVerifyCompositionForWrite(foreground, state.ClassName, state.NativeWindowHandle);
                            }
                        }
                    }
                    if (text == null && editable && document.Length <= captureLimit && before.Length <= captureLimit)
                    {
                        // Teams / WebView2 等仅提供 TextPattern 的富文本，当前行只能预览。
                        var caret = InputTextRange.FromAutomation(document, document, before, "");
                        if (caret != null) text = InputTextRange.LineAt(document, caret.Start).Text;
                    }
                }
            }
            else if (!selectionOnly && focusedTarget && state.HasKeyboardFocus &&
                (state.ControlType == ControlType.Edit || state.ControlType == ControlType.Document) && value != null)
            {
                var full = value.Current.Value;
                if (full.Length <= config.Translation.MaxInputCharacters && !full.Contains('\r') && !full.Contains('\n'))
                {
                    editRange = new(full, 0, full.Length); text = full;
                    canWrite = !value.Current.IsReadOnly &&
                        Native.CanVerifyCompositionForWrite(foreground, state.ClassName, state.NativeWindowHandle);
                }
            }
            if (text == null) continue;
            if (string.IsNullOrWhiteSpace(text) || text.Length > config.Translation.MaxInputCharacters) return null;
            capturedElement = candidate;
            return new InputSnapshot($"{pid}:{state.ProcessId}:{foreground}:{string.Join('.', candidate.GetRuntimeId())}",
                text, bounds, canWrite, IsSelection: selection, ControlBounds: controlBounds, EditRange: editRange);
        }
        return null;
    }

    private void ClearSelectionTarget() { selectionTarget = null; selectionFocus = null; selectionWindow = 0; }
    private static bool SameElement(AutomationElement? first, AutomationElement? second) =>
        first == null ? second == null : second != null && Automation.Compare(first, second);
    private static List<AutomationElement>? ForegroundPath(AutomationElement focused, nint foreground)
    {
        var path = new List<AutomationElement>();
        AutomationElement? node = focused;
        for (var depth = 0; depth < 64 && node != null; depth++)
        {
            path.Add(node);
            var handle = new nint(node.Current.NativeWindowHandle);
            if (handle != 0 && (handle == foreground || Native.IsChild(foreground, handle))) return path;
            node = TreeWalker.RawViewWalker.GetParent(node);
        }
        return null;
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
