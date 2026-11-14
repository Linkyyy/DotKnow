using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DotKnow.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using Matrix = System.Windows.Media.Matrix;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace DotKnow.App.Windows;

internal sealed record CapturedProblem(ImageAttachment Image, InputRect Bounds);

internal static class ScreenshotCapture
{
    public static async Task<CapturedProblem?> CaptureAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var window = new ScreenshotSelector();
        using var registration = token.Register(() => window.Dispatcher.BeginInvoke(new Action(window.Cancel)));
        var bounds = await window.SelectAsync();
        if (bounds == null) return null;
        // Let the compositor remove the selector before reading pixels. No clipboard or temporary file is involved.
        await Task.Delay(120, token);
        token.ThrowIfCancellationRequested();
        return new(ReadRegion(bounds), bounds);
    }

    internal static ImageAttachment ReadRegion(InputRect bounds)
    {
        var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
        var area = Rectangle.Intersect(desktop, new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
        if (area.Width < 8 || area.Height < 8 || (long)area.Width * area.Height > 32_000_000)
            throw new ArgumentException("截图区域过小或过大，请重新框选题目。");
        using var original = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(original))
            graphics.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        var scale = Math.Min(1, ImageAttachment.MaxEdge / (double)Math.Max(area.Width, area.Height));
        for (var attempt = 0; attempt < 6; attempt++, scale *= .75)
        {
            var width = Math.Max(1, (int)(area.Width * scale)); var height = Math.Max(1, (int)(area.Height * scale));
            using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, new Rectangle(0, 0, width, height));
            }
            using var stream = new MemoryStream(); resized.Save(stream, ImageFormat.Png);
            if (stream.Length <= ImageAttachment.MaxBytes) return new(stream.ToArray(), width, height);
        }
        throw new ArgumentException("截图无效或过大，请重新选择较小的题目区域。");
    }
}

/// <summary>One virtual-desktop overlay; selection coordinates always come from physical Win32 pixels.</summary>
internal sealed class ScreenshotSelector : Window
{
    private readonly TaskCompletionSource<InputRect?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly InputRect desktop;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent, Focusable = true };
    private readonly System.Windows.Shapes.Path shade = new() { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(145, 0, 0, 0)), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle outline = new() { Stroke = Brushes.YellowGreen, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border hint;
    private Native.Point start;
    private Native.Point? pressedPoint, releasedPoint, movedPoint;
    private bool dragging, finished;

    public ScreenshotSelector()
    {
        var area = System.Windows.Forms.SystemInformation.VirtualScreen;
        desktop = new(area.X, area.Y, area.Width, area.Height);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = true; Focusable = true; Cursor = Cursors.Cross;
        Title = UiText.Get("截图解题"); Content = canvas;
        Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
        canvas.Children.Add(shade); canvas.Children.Add(outline);
        hint = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock { Text = UiText.Get("拖动框选题目，松开后自动发送给视觉模型解题。Esc / 右键取消。"), Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 }, IsHitTestVisible = false };
        canvas.Children.Add(hint);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(TrackMouseMessage);
            Native.SetWindowPos(handle, new nint(-1), area.X, area.Y, area.Width, area.Height, 0x0040);
        };
        Loaded += (_, _) =>
        {
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), area.X, area.Y, area.Width, area.Height, 0x0040);
            Draw(null); Activate(); Keyboard.Focus(canvas);
        };
        SizeChanged += (_, _) => Draw(null);
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (pressedPoint is { } point) start = point; else Native.GetCursorPos(out start);
            dragging = true; CaptureMouse(); e.Handled = true;
        };
        PreviewMouseMove += (_, _) =>
        {
            if (!dragging) return;
            var end = movedPoint ?? CurrentPointer();
            Draw(ProblemSolving.Selection(start.X, start.Y, end.X, end.Y, desktop));
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false; ReleaseMouseCapture();
            var end = releasedPoint ?? CurrentPointer();
            var selection = ProblemSolving.Selection(start.X, start.Y, end.X, end.Y, desktop);
            if (selection != null) Finish(selection); else Draw(null);
            e.Handled = true;
        };
        PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
        LostMouseCapture += (_, _) => { if (dragging) Cancel(); };
        Closed += (_, _) => completion.TrySetResult(null);
        SystemEventsDisplayHandler = (_, _) => Dispatcher.BeginInvoke(new Action(Cancel));
    }
    private readonly EventHandler SystemEventsDisplayHandler;
    private static Native.Point CurrentPointer() { Native.GetCursorPos(out var point); return point; }
    private nint TrackMouseMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0100 && (int)wParam == 0x1b)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(Cancel));
            return 0;
        }
        if (message is 0x0200 or 0x0201 or 0x0202)
        {
            // lParam belongs to this queued event; GetCursorPos may already describe a later movement.
            var point = new Native.Point { X = (short)((long)lParam & 0xffff), Y = (short)(((long)lParam >> 16) & 0xffff) };
            if (Native.ClientToScreen(hwnd, ref point))
            {
                if (message == 0x0201) { pressedPoint = point; releasedPoint = null; }
                else if (message == 0x0202) releasedPoint = point;
                else movedPoint = point;
            }
        }
        return 0;
    }
    public Task<InputRect?> SelectAsync()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEventsDisplayHandler;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEventsDisplayHandler;
        Show(); return completion.Task;
    }
    internal void Cancel() => Finish(null);
    private void Finish(InputRect? bounds)
    {
        if (finished) return;
        finished = true; dragging = false; ReleaseMouseCapture();
        Hide(); completion.TrySetResult(bounds); Close();
    }
    private void Draw(InputRect? selection)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        outline.Visibility = selection == null ? Visibility.Collapsed : Visibility.Visible;
        if (selection != null)
        {
            var p = transform.Transform(new Point(selection.X - desktop.X, selection.Y - desktop.Y));
            var size = transform.Transform(new Vector(selection.Width, selection.Height));
            var rect = new Rect(p.X, p.Y, size.X, size.Y);
            geometry.Children.Add(new RectangleGeometry(rect));
            Canvas.SetLeft(outline, rect.X); Canvas.SetTop(outline, rect.Y); outline.Width = rect.Width; outline.Height = rect.Height;
        }
        shade.Data = geometry;
        // Place instructions on the monitor containing the pointer, even with negative desktop origins.
        Native.GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        var hintPoint = transform.Transform(new Point(screen.Left - desktop.X + 24, screen.Top - desktop.Y + 24));
        Canvas.SetLeft(hint, hintPoint.X); Canvas.SetTop(hint, hintPoint.Y);
        hint.Visibility = dragging ? Visibility.Collapsed : Visibility.Visible;
    }
}
