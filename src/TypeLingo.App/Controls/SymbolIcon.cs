using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeLingo.App.Controls;

/// <summary>统一的线性矢量图标，不依赖系统图标字体或彩色表情。</summary>
public sealed class SymbolIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(SymbolIcon), new FrameworkPropertyMetadata("settings", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    static SymbolIcon()
    {
        WidthProperty.OverrideMetadata(typeof(SymbolIcon), new FrameworkPropertyMetadata(18d));
        HeightProperty.OverrideMetadata(typeof(SymbolIcon), new FrameworkPropertyMetadata(18d));
        ForegroundProperty.OverrideMetadata(typeof(SymbolIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (TryFindResource("Icon." + Kind) is not Geometry geometry) return;
        var pen = new Pen(Foreground ?? (Brush)FindResource("TextBrush"), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        drawing.DrawGeometry(null, pen, geometry); drawing.Pop();
    }
}
