using System.IO;
using Microsoft.UI.Dispatching;
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
        // 启动即捕获未处理异常：把堆栈写入崩溃日志，供自包含发布定位启动失败根因
        UnhandledException += (_, e) =>
        {
            try
            {
                var log = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n\n");
            }
            catch { /* 日志写入失败不影响崩溃上报 */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var log = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(log, $"[{DateTime.Now:O}] AppDomain: {e.ExceptionObject}\n\n");
            }
            catch { }
        };

        // 第二个进程只负责唤醒已有窗口，随即退出，避免同一用户并存多份播放实例
        if (!SingleInstance.Acquire())
        {
            Environment.Exit(0);
            return;
        }

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

        // 窗口已就绪，开始监听后续启动请求：把已有窗口从托盘/最小化状态唤回前台
        SingleInstance.StartListening(() => MainWindow?.DispatcherQueue.TryEnqueue(ShowFromTray));
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
        // 放行关闭后窗口随消息循环销毁；单实例监听等后台源仍会让消息循环驻留，
        // 需显式退出进程，否则窗口关闭后留下未回收的僵尸进程占用资源与文件句柄。
        // 这里直接同步退出：窗口即将销毁，无需等待 DispatcherQueue 回调（关闭时它可能已停止）。
        Environment.Exit(0);
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
