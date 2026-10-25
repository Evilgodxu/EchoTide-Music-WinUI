using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace YiChao.Ui.Component;

/// <summary>
/// Material 图标：以路径数据绘图，按 24 格坐标系等比缩放到指定尺寸。
///
/// 路径数据直接从忆潮的 AppIcons 取用，两端的图形因此完全一致；
/// 尺寸只决定外框大小，图形在框内的占比由 24 格自身的内边距决定。
/// </summary>
public sealed partial class MaterialIcon : UserControl
{
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData),
        typeof(string),
        typeof(MaterialIcon),
        new PropertyMetadata(string.Empty, OnIconDataChanged));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(MaterialIcon),
        new PropertyMetadata(32.0, OnIconSizeChanged));

    public static readonly DependencyProperty IconFillProperty = DependencyProperty.Register(
        nameof(IconFill),
        typeof(Brush),
        typeof(MaterialIcon),
        new PropertyMetadata(null, OnIconFillChanged));

    public MaterialIcon()
    {
        InitializeComponent();
        Scaler.Width = IconSize;
        Scaler.Height = IconSize;
        Glyph.Fill = IconFill ?? new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    /// <summary>SVG 路径数据，坐标按 24×24 格给出。</summary>
    public string IconData
    {
        get => (string)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>填充色；不给则按忆潮首页的纯白处理。</summary>
    public Brush IconFill
    {
        get => (Brush)GetValue(IconFillProperty);
        set => SetValue(IconFillProperty, value);
    }

    private static void OnIconDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (MaterialIcon)d;
        var data = e.NewValue as string;
        icon.Glyph.Data = string.IsNullOrEmpty(data)
            ? null
            : (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
    }

    private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (MaterialIcon)d;
        var size = (double)e.NewValue;
        icon.Scaler.Width = size;
        icon.Scaler.Height = size;
    }

    private static void OnIconFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (MaterialIcon)d;
        icon.Glyph.Fill = e.NewValue as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.White);
    }
}
