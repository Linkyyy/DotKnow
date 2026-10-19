using System.Windows;
using System.Windows.Controls;

namespace TypeLingo.App.Controls;

// 统一设置卡片的标题、说明和图标，内容区域保留标准 WPF 控件的键盘与无障碍行为。
public sealed class SettingCard : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingCard), new PropertyMetadata("\uE713"));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
