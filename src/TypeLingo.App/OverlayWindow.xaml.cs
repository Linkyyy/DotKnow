using System.Windows;
using System.Windows.Interop;
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
    private InputRect? anchor;
    private bool requesting;
    private string statusSource = "原文保持不变";
    public OverlayWindow(ReplacementService replacement, Func<AppSettings> settings)
    {
        this.replacement = replacement; this.settings = settings;
        InitializeComponent(); SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x00000080);
            HwndSource.FromHwnd(handle)?.AddHook(NoActivation);
        };
        SizeChanged += (_, _) => Position();
        UiText.Changed += RefreshLanguage;
    }
    private static nint NoActivation(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == Native.WmMouseActivate) { handled = true; return Native.MaNoActivate; } return 0; }
    public void Display(Preview? preview)
    {
        CancelConfirmation(); current = preview;
        if (preview == null) { TranslationText.Text = ""; Hide(); return; }
        anchor = preview.Snapshot.Bounds;
        TranslationText.Text = preview.Result.Text;
        TranslationText.FontSize = settings().Overlay.FontSize;
        RefreshLanguage();
        CopyButton.IsEnabled = true; ReplaceButton.IsEnabled = preview.Snapshot.CanWrite;
        ShowStatus(preview.FromCache ? "本地缓存 · 原文保持不变" : "译文预览 · 原文保持不变");
        Width = settings().Overlay.MaxWidth; Opacity = settings().Overlay.Opacity;
        Show(); Position();
    }
    public void MoveAnchor(InputRect bounds) { anchor = bounds; if (IsVisible) Position(); }
    public void ShowStatus(string message) { statusSource = message; StatusText.Text = UiText.Get(message); }
    private void RefreshLanguage()
    {
        var target = current?.Request.TargetLanguage ?? settings().Translation.TargetLanguage;
        LanguageLabel.Text = UiText.Get(target switch { "ja" => "日语", "zh" => "中文", _ => "英语" });
        StatusText.Text = UiText.Get(statusSource);
    }
    public void Loading(InputSnapshot? snapshot, string message)
    {
        if (current != null || snapshot == null || string.IsNullOrWhiteSpace(snapshot.Text) || snapshot.IsComposing || snapshot.IsSensitive) return;
        anchor = snapshot.Bounds; TranslationText.Text = "…"; RefreshLanguage(); ShowStatus(message);
        CopyButton.IsEnabled = ReplaceButton.IsEnabled = false;
        Width = settings().Overlay.MaxWidth; Opacity = settings().Overlay.Opacity;
        Show(); Position();
    }
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
            var result = await replacement.PrepareAsync();
            if (result == null) { ShowStatus("输入已变化、正在组字或不支持安全写入；请重新确认或复制译文。"); return; }
            Display(result.Preview); ticket = result;
            OriginalText.Text = result.Preview.Snapshot.Text;
            Confirmation.Visibility = Visibility.Visible; Position();
        }
        catch (Exception) { ShowStatus("无法安全准备替换，请重新确认或复制译文。"); }
        finally { requesting = false; }
    }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ticket == null) return;
        var captured = ticket; ticket = null; Confirmation.Visibility = Visibility.Collapsed;
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
    private void Cancel_Click(object sender, RoutedEventArgs e) { CancelConfirmation(); ShowStatus("已取消 · 原文保持不变"); }
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyTranslation();
    private async void Replace_Click(object sender, RoutedEventArgs e) => await RequestReplacementAsync();

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
        var chosen = OverlayPlacement.Place(r, new(area.Left, area.Top, area.Width, area.Height), width, height, settings().Overlay.Position);
        // UIA 和 Screen 使用屏幕物理像素；每个显示器的 DPI 换算交由 WPF 处理。
        Native.SetWindowPos(handle, new nint(-1), (int)chosen.X, (int)chosen.Y, 0, 0, 0x0001 | 0x0010);
    }
}
