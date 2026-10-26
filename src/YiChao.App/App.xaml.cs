using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using YiChao.Data.Settings;
using YiChao.Ui.Component.Window;
using YiChao.Ui.Theme;

namespace YiChao;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>主窗口：全屏切换与文件夹选择器需要窗口句柄，故由应用级持有。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>首页播放中枢：窗口级快捷键（空格与左右键）在浮层收起时也要能播控。</summary>
    public static Screens.Home.HomeViewModel? Player { get; set; }

    /// <summary>首页骨架：Esc 需要按页面层级回退，故由应用级持有。</summary>
    public static Screens.Home.HomeScreen? Home { get; set; }

    /// <summary>全局配置：启动时读一次，界面各处共用同一份实例。</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>通知区图标：关闭行为选托盘时显示，右键菜单可直接退出。</summary>
    public static TrayIcon? Tray { get; private set; }

    /// <summary>经托盘退出时置位：窗口关闭流程据此放行，不再拦截成最小化。</summary>
    public static bool IsExiting { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 主题与语言必须在界面构造之前定下来，否则首帧会先按系统默认值画一遍再改
        Settings = AppSettings.Load();
        AppTheme.Initialize(Settings.Theme);
        Strings.Initialize(Settings.Language);

        MainWindow = new MainWindow();
        MainWindow.Activate();
        AppTheme.Apply();

        Tray = new TrayIcon(ShowFromTray, ExitFromTray);
        Tray.SetVisible(Settings.CloseBehavior == CloseBehavior.MinimizeToTray);
    }

    /// <summary>关闭按钮与系统关闭共用：按设置决定直接退出还是缩到托盘。</summary>
    public static void RequestClose()
    {
        if (IsExiting)
        {
            return;
        }

        if (Settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            HideToTray();
        }
        else
        {
            ExitFromTray();
        }
    }

    /// <summary>窗口关闭事件：托盘模式下拦截并隐藏，退出模式下放行并回收图标。</summary>
    public static void OnWindowClosing(AppWindowClosingEventArgs args)
    {
        if (!IsExiting && Settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        Tray?.Dispose();
        Tray = null;
    }

    /// <summary>隐藏主窗口但保留进程与播放：托盘图标在隐藏期间保持可见。</summary>
    public static void HideToTray()
    {
        if (MainWindow is null)
        {
            return;
        }

        Tray?.SetVisible(true);
        MainWindow.AppWindow.Hide();
    }

    /// <summary>从托盘恢复主窗口；隐藏期间可能同时被系统最小化，一并还原。</summary>
    public static void ShowFromTray()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.AppWindow.Show();
        if (MainWindow.AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        MainWindow.Activate();
    }

    /// <summary>结束进程：先摘掉托盘图标，再走正常关窗流程。</summary>
    public static void ExitFromTray()
    {
        IsExiting = true;
        Tray?.Dispose();
        Tray = null;
        MainWindow?.Close();
    }

    /// <summary>关闭行为变更后同步托盘图标显隐。</summary>
    public static void ApplyCloseBehavior(CloseBehavior behavior)
    {
        Tray?.SetVisible(behavior == CloseBehavior.MinimizeToTray);
    }

    /// <summary>窗口全屏切换：把忆潮的横屏切换在桌面端落到全屏，功能等价而图标沿用。</summary>
    public static void ToggleFullScreen()
    {
        var appWindow = MainWindow?.AppWindow;
        if (appWindow is null)
        {
            return;
        }

        var fullScreen = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        appWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
    }
}
