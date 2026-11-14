using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DotKnow.App.Windows;

namespace DotKnow.App;

public partial class AssistantWindow
{
    private bool compactView;
    private bool? compactOverride;
    private bool IsSolveTask => SessionImage != null || TaskInput.SelectedValue as string == "solve";
    private bool WantsCompact => IsSolveTask && (compactOverride ?? settings().Assistant.ExamMode);

    private nint CompactMouseHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (compactView && message == Native.WmMouseActivate)
        {
            handled = true;
            return new nint(Native.MaNoActivate);
        }
        return 0;
    }
    private void SetCompactView(bool compact)
    {
        compactView = compact;
        StandardPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ContentFrame.Padding = compact ? new Thickness(10) : new Thickness(18, 14, 18, 14);
        ContentFrame.BorderThickness = compact ? new Thickness(1) : new Thickness(0);
        ContentFrame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BorderBrush");
        ContentFrame.CornerRadius = new CornerRadius(compact ? 8 : 0);
        WindowStyle = compact ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = compact ? ResizeMode.NoResize : ResizeMode.CanResize;
        CompactModeButton.Visibility = IsSolveTask && (current != null || IsQuerying) ? Visibility.Visible : Visibility.Collapsed;
        RefreshCompactText();
    }
    private void RefreshCompactText()
    {
        if (!HasSession) return;
        CompactAnswerText.Text = current?.Result.Text ?? QueryStatus.Text;
        CompactAnswerText.FontSize = settings().Overlay.FontSize;
        CompactAnswerButton.Cursor = current != null ? Cursors.Hand : Cursors.Arrow;
    }
    private void MeasureCompact(double availableWidth, double availableHeight)
    {
        // Measure the actual rendered text; a short answer must not reserve space for screenshots or editors.
        MaxWidth = Math.Max(1, Math.Min(320, availableWidth));
        MaxHeight = Math.Max(1, Math.Min(240, availableHeight));
        MinWidth = Math.Min(96, MaxWidth); MinHeight = Math.Min(32, MaxHeight);
        var textWidth = Math.Max(1, MaxWidth - 22);
        CompactAnswerText.MaxWidth = textWidth;
        CompactAnswerText.Measure(new Size(textWidth, double.PositiveInfinity));
        Width = Math.Clamp(CompactAnswerText.DesiredSize.Width + 22, MinWidth, MaxWidth);
        CompactAnswerText.MaxWidth = Math.Max(1, Width - 22);
        CompactAnswerText.Measure(new Size(Math.Max(1, Width - 22), double.PositiveInfinity));
        CompactFeedback.Measure(new Size(Math.Max(1, Width - 22), double.PositiveInfinity));
        var feedback = CompactFeedback.Visibility == Visibility.Visible ? CompactFeedback.DesiredSize.Height : 0;
        CompactScroll.MaxHeight = Math.Max(1, MaxHeight - 22 - feedback);
        Height = Math.Clamp(CompactAnswerText.DesiredSize.Height + 22 + feedback, MinHeight, MaxHeight);
    }
    private void CompactMode_Click(object sender, RoutedEventArgs e)
    {
        if (!HasSession || !IsSolveTask) return;
        compactOverride = true; CompactFeedback.Visibility = Visibility.Collapsed;
        SetCompactView(true); Position(SessionSnapshot!.Bounds);
    }
    private void ExamDetails_Click(object sender, RoutedEventArgs e)
    {
        if (!HasSession) return;
        compactOverride = false; SetCompactView(false);
        Position(SessionSnapshot!.Bounds); Activate();
    }
    private void CompactCopy_Click(object sender, RoutedEventArgs e) => CopyAnswer();
}
