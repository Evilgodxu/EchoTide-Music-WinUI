using Microsoft.UI.Xaml.Controls;

namespace YiChao.Screens.Home.Component.Bar;

/// <summary>
/// 悬浮标题栏：只显示曲名与艺术家。不持有业务状态，也不上报交互——
/// 操作入口已全部交给系统标题按钮与底部控制栏，这里只承担信息呈现。
/// </summary>
public sealed partial class HomeTopBar : UserControl
{
    public HomeTopBar()
    {
        InitializeComponent();
    }

    public void SetTrack(string? title, string? artist)
    {
        TitleText.Text = title ?? string.Empty;
        ArtistText.Text = artist ?? string.Empty;
    }
}
