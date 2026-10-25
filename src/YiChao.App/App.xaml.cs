using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using YiChao.Data.Settings;
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 主题与语言必须在界面构造之前定下来，否则首帧会先按系统默认值画一遍再改
        Settings = AppSettings.Load();
        AppTheme.Initialize(Settings.Theme);
        Strings.Initialize(Settings.Language);

        MainWindow = new MainWindow();
        MainWindow.Activate();
        AppTheme.Apply();
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
