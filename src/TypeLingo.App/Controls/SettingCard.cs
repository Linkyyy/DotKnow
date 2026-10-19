using System.Windows;
using System.Windows.Controls;

namespace TypeLingo.App.Controls;

// 统一分隔行的标题和说明，内容区域保留标准 WPF 控件的键盘与无障碍行为。
public sealed class SettingCard : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
