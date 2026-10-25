using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Settings.Component.Entry;

/// <summary>
/// 一行设置项：图标 + 标题 + 副标题，整行可点。
///
/// 图标走内容槽而不是路径字符串——WinUI 没有把路径文本解析成 Geometry 的公开入口，
/// 由调用方在 XAML 里给一个 Path，尺寸与着色也就一并由调用方决定。
/// </summary>
public sealed partial class SettingsEntryView : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingsEntryView),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(SettingsEntryView),
        new PropertyMetadata(string.Empty, OnSubtitleChanged));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(object),
        typeof(SettingsEntryView),
        new PropertyMetadata(null));

    public SettingsEntryView()
    {
        InitializeComponent();
    }

    public event EventHandler? Clicked;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingsEntryView)d).TitleText.Text = e.NewValue as string ?? string.Empty;

    private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingsEntryView)d).SubtitleText.Text = e.NewValue as string ?? string.Empty;

    /// <summary>按当前主题刷新文字层级色：标题用主前景，副标题与图标用次级前景。</summary>
    public void ApplyTheme()
    {
        TitleText.Foreground = new SolidColorBrush(AppTheme.OnSurface);
        SubtitleText.Foreground = new SolidColorBrush(AppTheme.OnSurfaceVariant);

        if (Icon is Shape shape)
        {
            shape.Fill = new SolidColorBrush(AppTheme.OnSurfaceVariant);
        }
    }

    private void OnClick(object sender, RoutedEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
}
