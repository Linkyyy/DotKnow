using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DotKnow.App.Controls;

/// <summary>Size the closed selector to its selected label; long options can still use a wider popup.</summary>
public static class ComboBoxSizing
{
    public static readonly DependencyProperty FitSelectionProperty = DependencyProperty.RegisterAttached(
        "FitSelection", typeof(bool), typeof(ComboBoxSizing), new PropertyMetadata(false, Changed));
    public static bool GetFitSelection(DependencyObject target) => (bool)target.GetValue(FitSelectionProperty);
    public static void SetFitSelection(DependencyObject target, bool value) => target.SetValue(FitSelectionProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ComboBox combo) return;
        if ((bool)e.NewValue) { combo.Loaded += Resize; combo.SelectionChanged += Resize; }
        else { combo.Loaded -= Resize; combo.SelectionChanged -= Resize; }
    }
    private static void Resize(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox { IsEditable: false } combo) return;
        var item = combo.SelectedItem;
        var label = string.IsNullOrEmpty(combo.DisplayMemberPath) ? item?.ToString() :
            item?.GetType().GetProperty(combo.DisplayMemberPath)?.GetValue(item)?.ToString();
        if (label == null) return;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, combo.FlowDirection,
            new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch), combo.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(combo).PixelsPerDip);
        combo.SetCurrentValue(FrameworkElement.WidthProperty, Math.Clamp(Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 52, 86, 260));
    }
}
