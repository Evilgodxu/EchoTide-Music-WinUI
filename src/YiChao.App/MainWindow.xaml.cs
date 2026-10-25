using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using YiChao.Ui.Theme;

namespace YiChao;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 780;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));
        ApplyTitleBarColors();
        ApplyWindowIcon();

        if (Content is UIElement root)
        {
            SetupShortcuts(root);
        }
    }

    /// <summary>
    /// 窗口与任务栏图标取自忆潮的启动图标。非打包应用的任务栏默认用 exe 内嵌图标，
    /// 这里显式再设一次，使带走 Assets 目录的免安装副本也保有同一枚图标。
    /// </summary>
    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    /// <summary>
    /// 切到沉浸式标题栏：客户区铺到窗口最顶，系统标题栏整条收掉。
    ///
    /// 系统那排标题按钮必须收掉——它的前景与底色由 DWM 绘制、不接受 alpha，
    /// 无法随浮层淡出。收掉后由页面里的 CaptionButtons 自绘三枚等尺寸按钮来接管。
    /// 拖拽区交给页面顶部一条常驻薄条，浮层收起后窗口仍然拖得动。
    /// </summary>
    public void UseImmersiveTitleBar(UIElement dragRegion)
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        SetTitleBar(dragRegion);
    }

    /// <summary>
    /// 窗口级传输快捷键：浮层收起时也能播控，不必先点出控制栏。
    ///
    /// 走 PreviewKeyDown 而不是 KeyboardAccelerator：后者会在所属元素上自动挂出按键提示，
    /// 挂在根元素上就成了鼠标移到窗口任意处都冒出一个「F11」角标。
    /// 预览阶段接管并标记已处理，空格因此不会同时激活当前聚焦的按钮。
    /// </summary>
    private void SetupShortcuts(UIElement root)
    {
        // 无焦点元素时按键不进入路由，根元素自己收下焦点作为兜底
        root.IsTabStop = true;
        Activated += (_, _) => root.Focus(FocusState.Programmatic);
        root.PreviewKeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.Space:
                    App.Player?.TogglePlayPause();
                    break;
                case VirtualKey.Left:
                    App.Player?.Previous();
                    break;
                case VirtualKey.Right:
                    App.Player?.Next();
                    break;
                case VirtualKey.F11:
                    App.ToggleFullScreen();
                    break;
                case VirtualKey.Escape when App.Home?.HandleBackKey() == true:
                    break;
                default:
                    return;
            }

            args.Handled = true;
        };
    }

    /// <summary>
    /// 标题栏底色与前景：系统标题栏已收掉，这里只保留背景透明的设定，
    /// 避免某些状态下系统在顶边补一条不透明的色带。
    /// </summary>
    public void ApplyTitleBarColors() => ApplyTitleBarColors(AppTheme.IsDark);

    public void ApplyTitleBarColors(bool dark)
    {
        var titleBar = AppWindow.TitleBar;
        var foreground = dark ? Color.FromArgb(255, 0xFF, 0xFF, 0xFF) : Color.FromArgb(255, 0x1F, 0x23, 0x28);
        var inactiveForeground = Color.FromArgb(255, 0x8B, 0x94, 0x9E);
        var transparent = Color.FromArgb(0, 0, 0, 0);

        titleBar.BackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 0x21, 0x26, 0x2D);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 0x30, 0x36, 0x3D);
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
