using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DotKnow.App.Controls;

/// <summary>直接绘制用户选定的 C 矢量主标，随配色切换且不依赖位图缩放。</summary>
public sealed class BrandMark : Control
{
    static BrandMark()
    {
        WidthProperty.OverrideMetadata(typeof(BrandMark), new FrameworkPropertyMetadata(32d));
        HeightProperty.OverrideMetadata(typeof(BrandMark), new FrameworkPropertyMetadata(32d));
        ForegroundProperty.OverrideMetadata(typeof(BrandMark), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (TryFindResource("BrandMarkGeometry") is not Geometry geometry) return;
        drawing.PushTransform(new ScaleTransform(ActualWidth / 1024, ActualHeight / 1024));
        drawing.DrawGeometry(Foreground ?? (Brush)FindResource("TextBrush"), null, geometry);
        drawing.Pop();
    }
}
