using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using TypeLingo.App.Windows;
using TypeLingo.Core;
using Forms = System.Windows.Forms;

namespace TypeLingo.App;

public partial class OverlayWindow : Window
{
    private Preview? current;
    private ReplacementTicket? ticket;
    private readonly ReplacementService replacement;
    private readonly Func<AppSettings> settings;
    private readonly Action<Preview>? saveFavorite;
    private InputRect? anchor;
    private InputRect? avoid;
    private readonly DispatcherTimer hideTimer = new();
    private bool requesting;
    private string statusSource = "原文保持不变";
    public OverlayWindow(ReplacementService replacement, Func<AppSettings> settings, Action<Preview>? saveFavorite = null)
    {
        this.replacement = replacement; this.settings = settings; this.saveFavorite = saveFavorite;
        InitializeComponent(); SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x00000080);
            HwndSource.FromHwnd(handle)?.AddHook(NoActivation);
        };
        SizeChanged += (_, _) => Position();
        UiText.Changed += RefreshLanguage;
        hideTimer.Tick += (_, _) => { if (!IsMouseOver && ticket == null) Dismiss(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) hideTimer.Stop(); };
    }
    private static nint NoActivation(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == Native.WmMouseActivate) { handled = true; return Native.MaNoActivate; } return 0; }
    public void Display(Preview? preview)
    {
        CancelConfirmation(); hideTimer.Stop(); current = preview;
        LoadingProgress.Visibility = Visibility.Collapsed;
        if (preview == null) { TranslationText.Text = ""; Hide(); return; }
        anchor = preview.Snapshot.Bounds; avoid = preview.Snapshot.ControlBounds;
        TranslationText.Text = preview.Result.Text;
        TranslationText.FontSize = settings().Overlay.FontSize;
        RefreshLanguage();
        CopyButton.Visibility = Visibility.Visible; CopyButton.IsEnabled = true; ReplaceButton.Visibility = preview.Snapshot.CanWrite ? Visibility.Visible : Visibility.Collapsed; ReplaceButton.IsEnabled = preview.Snapshot.CanWrite;
        FavoriteButton.Visibility = saveFavorite == null ? Visibility.Collapsed : Visibility.Visible;
        FavoriteButton.IsEnabled = true;
        TranslationText.Cursor = preview.Snapshot.CanWrite ? Cursors.Hand : Cursors.Arrow;
        TranslationText.ToolTip = UiText.Get(preview.Snapshot.CanWrite ? "点击译文准备替换选区或当前行" : "当前控件仅支持复制与收藏");
        ShowStatus(preview.FromCache ? "本地缓存 · 原文保持不变" : "译文预览 · 原文保持不变");
        ConfigureBubble(); Show(); UpdateLayout(); Position(); RestartTimer();
    }
    public void MoveAnchor(InputRect bounds, InputRect? controlBounds = null) { anchor = bounds; avoid = controlBounds; if (IsVisible) Position(); }
    public void ShowStatus(string message) { statusSource = message; Bubble.ToolTip = UiText.Get(message); }
    private void RefreshLanguage() => Bubble.ToolTip = UiText.Get(statusSource);
    public void Loading(InputSnapshot? snapshot, string message)
    {
        if (IsVisible) ShowStatus(message);
        if (current == null && snapshot is { IsComposing: false, IsSensitive: false } && !string.IsNullOrWhiteSpace(snapshot.Text) &&
            message is "等待输入稳定…" or "正在翻译…")
        {
            if (!settings().Overlay.ShowLoading) { Hide(); return; }
            anchor = snapshot.Bounds; avoid = snapshot.ControlBounds;
            TranslationText.Text = UiText.Get(message); TranslationText.Cursor = Cursors.Arrow; TranslationText.ToolTip = null;
            LoadingProgress.Visibility = Visibility.Visible;
            CopyButton.Visibility = ReplaceButton.Visibility = FavoriteButton.Visibility = Visibility.Collapsed;
            hideTimer.Stop(); ConfigureBubble(); Show(); UpdateLayout(); Position(); return;
        }
        if (current != null || snapshot == null || snapshot.IsComposing || snapshot.IsSensitive || string.IsNullOrWhiteSpace(snapshot.Text) ||
            message is "等待输入稳定…" or "正在翻译…" or "本地缓存 · 未调用 API" or "译文就绪 · 原文保持不变") return;
        anchor = snapshot.Bounds; avoid = snapshot.ControlBounds;
        LoadingProgress.Visibility = Visibility.Collapsed;
        TranslationText.Text = UiText.Get("翻译暂不可用"); ShowStatus(message);
        CopyButton.Visibility = ReplaceButton.Visibility = FavoriteButton.Visibility = Visibility.Collapsed;
        ConfigureBubble(); Show(); UpdateLayout(); Position(); RestartTimer();
    }
    private void ConfigureBubble()
    {
        var config = settings().Overlay;
        MaxWidth = Math.Max(config.MaxWidth, ticket == null ? 120 : 340);
        MaxHeight = ticket == null ? 240 : 500;
        TranslationText.MaxWidth = Math.Max(32, MaxWidth - 68 -
            (ReplaceButton.Visibility == Visibility.Visible ? 24 : 0) - (FavoriteButton.Visibility == Visibility.Visible ? 24 : 0));
        TranslationText.FontSize = config.FontSize;
        ApplyTransparency();
    }
    private void ApplyTransparency()
    {
        // 透明度仅作用于背景，文字保持清晰；鼠标悬停和确认替换时恢复实色。
        var color = ((SolidColorBrush)FindResource("CanvasBrush")).Color;
        color.A = (byte)Math.Round(255 * (IsMouseOver || ticket != null ? 1 : settings().Overlay.Opacity));
        Bubble.Background = new SolidColorBrush(color);
        Actions.Opacity = IsMouseOver ? 1 : .55;
    }
    private void RestartTimer()
    {
        hideTimer.Stop();
        if (settings().Overlay.AutoHideSeconds <= 0) return;
        hideTimer.Interval = TimeSpan.FromSeconds(settings().Overlay.AutoHideSeconds); hideTimer.Start();
    }
    private void Bubble_MouseEnter(object sender, MouseEventArgs e) { hideTimer.Stop(); ApplyTransparency(); }
    private void Bubble_MouseLeave(object sender, MouseEventArgs e) { ApplyTransparency(); RestartTimer(); }
    public void Dismiss() { CancelConfirmation(); current = null; hideTimer.Stop(); Hide(); }
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Dismiss();
    public void CopyTranslation()
    {
        if (current == null) return;
        try { System.Windows.Clipboard.SetText(current.Result.Text); ShowStatus("译文已复制"); }
        catch (System.Runtime.InteropServices.COMException) { ShowStatus("剪贴板正忙，请重试。"); }
    }
    public async Task RequestReplacementAsync()
    {
        if (requesting) return;
        requesting = true;
        try
        {
            CancelConfirmation();
            var result = current is { } displayed ? await replacement.PreparePreviewAsync(displayed) : await replacement.PrepareAsync();
            if (result == null) { ShowStatus("输入已变化、正在组字或不支持安全写入；请重新确认或复制译文。"); return; }
            Display(result.Preview); ticket = result;
            OriginalText.Text = result.Preview.Snapshot.Text;
            Confirmation.Visibility = Visibility.Visible; ConfigureBubble(); UpdateLayout(); Position();
        }
        catch (Exception) { ShowStatus("无法安全准备替换，请重新确认或复制译文。"); }
        finally { requesting = false; }
    }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ticket == null) return;
        var captured = ticket; ticket = null; Confirmation.Visibility = Visibility.Collapsed; ConfigureBubble(); RestartTimer();
        ConfirmButton.IsEnabled = false;
        try
        {
            var success = await replacement.ConfirmAsync(captured);
            if (success) { current = null; Hide(); }
            else ShowStatus("目标、内容或配置已变化。未替换，请重新确认。");
        }
        catch (Exception) { ShowStatus("控件拒绝安全写入，未执行其他输入操作。请复制译文。"); }
        finally { ConfirmButton.IsEnabled = true; }
    }
    private void CancelConfirmation()
    { if (ticket != null) replacement.Cancel(ticket); ticket = null; Confirmation.Visibility = Visibility.Collapsed; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { CancelConfirmation(); ConfigureBubble(); RestartTimer(); ShowStatus("已取消 · 原文保持不变"); }
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyTranslation();
    private async void Replace_Click(object sender, RoutedEventArgs e) => await RequestReplacementAsync();
    private async void Translation_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    { if (current?.Snapshot.CanWrite == true) { e.Handled = true; await RequestReplacementAsync(); } }
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || saveFavorite == null) return;
        try { saveFavorite(current); FavoriteButton.IsEnabled = false; ShowStatus("已收藏，可在本地收藏与复习中查看。"); }
        catch (Exception) { ShowStatus("收藏失败，请检查本地文件与存储权限。"); }
    }

    private void Position()
    {
        if (!IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var r = anchor ?? new InputRect(0, 0, 0, 0);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)r.X, (int)r.Y));
        var area = screen.WorkingArea;
        var chosen = OverlayPlacement.Place(r, new(area.Left, area.Top, area.Width, area.Height), width, height, settings().Overlay.Position, avoid);
        // UIA 和 Screen 使用屏幕物理像素；每个显示器的 DPI 换算交由 WPF 处理。
        Native.SetWindowPos(handle, new nint(-1), (int)chosen.X, (int)chosen.Y, 0, 0, 0x0001 | 0x0010);
    }
}
