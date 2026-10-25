using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YiChao.Screens.Home.Component.Player;

/// <summary>
/// 横屏融合封面：四边按竖屏封面下缘的同一条曲线羽化，与同源背景无缝衔接，
/// 因此呈现为一片融入背景的图，而不是一张有硬边的卡片。
///
/// 羽化已在解码时烘进像素（见 CoverLoader），这里只负责铺图，不做任何合成。
/// </summary>
public sealed partial class CoverBlend : UserControl
{
    public CoverBlend()
    {
        InitializeComponent();
    }

    /// <summary>换图；无封面时整幅收起，露出同源背景。</summary>
    public void SetCover(ImageSource? image)
    {
        CoverImage.Source = image;
        Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
