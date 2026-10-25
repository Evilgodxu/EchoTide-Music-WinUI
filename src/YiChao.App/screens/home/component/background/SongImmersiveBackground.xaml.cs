using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using YiChao.Data.Metadata;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Background;

/// <summary>
/// 歌曲沉浸背景：整屏一张底色，取自封面边缘环的三段色，自上而下铺成渐变并叠一层恒定压暗。
///
/// 三段色与封面外缘同源，封面四边羽化带因此接得上同色系的背景，不会现出硬边；
/// 压暗层只负责给前景文字与图标留出对比，不改变色相。
/// 无封面时退回深色主题的两段渐变——那是与页面底色同源的近黑，切歌取不到色时不会整屏发黑。
/// </summary>
public sealed partial class SongImmersiveBackground : UserControl
{
    /// <summary>静态底色压暗强度：与 Android 端 DIM_MIN_ALPHA 同值。</summary>
    private const byte DimAlpha = 0x26;

    public SongImmersiveBackground()
    {
        InitializeComponent();
        ApplyPalette(null);
    }

    public void ApplyPalette(CoverPalette? palette)
    {
        BaseLayer.Fill = palette is null ? DefaultGradient() : PaletteGradient(palette);
        DimLayer.Fill = new SolidColorBrush(Color.FromArgb(DimAlpha, 0, 0, 0));
    }

    private static LinearGradientBrush PaletteGradient(CoverPalette palette) => new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1),
        GradientStops = new GradientStopCollection
        {
            new() { Offset = 0, Color = palette.Top },
            new() { Offset = 0.5, Color = palette.Middle },
            new() { Offset = 1, Color = palette.Bottom },
        },
    };

    private static LinearGradientBrush DefaultGradient() => new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1),
        GradientStops = new GradientStopCollection
        {
            new() { Offset = 0, Color = Palette.Surface },
            new() { Offset = 1, Color = Palette.SurfaceVariant },
        },
    };
}
