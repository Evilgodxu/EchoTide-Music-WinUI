using System.Diagnostics;
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

        // 第二个进程只负责唤醒已有窗口，随即退出；这一步在 Program.Main 里完成，
        // 不能放在构造函数——那等于在 WinRT 启动回调里退出，会崩出残骸进程
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

    /// <summary>收尾已经开始：托盘已摘掉、看门狗已挂上，重复触发不再重来一遍。</summary>
    private static bool _shutdownStarted;

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

        // 窗口已就绪，开始监听后续启动请求：把已有窗口从托盘/最小化状态唤回前台；
        // 以及安装程序的退出请求——常驻托盘的实例关不掉窗口，只能由它自己走退出流程。
        // 监听回调在后台线程上跑，这里先把 UI 队列交出去：从后台线程读 Window.DispatcherQueue
        // 属于跨公寓访问，会以 RPC_E_WRONG_THREAD 崩掉正在运行的实例。
        var uiQueue = DispatcherQueue.GetForCurrentThread();
        SingleInstance.StartListening(
            () => uiQueue.TryEnqueue(ShowFromTray),
            () => uiQueue.TryEnqueue(ExitFromTray));
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

    /// <summary>窗口关闭事件：托盘模式下拦截并隐藏，退出模式下放行并延后收尾。</summary>
    public static void OnWindowClosing(AppWindowClosingEventArgs args)
    {
        if (!IsExiting && Settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        // 取消这次关闭，改在下一轮 UI 队列里退出：此刻仍在 WinUI 的关闭回调内部，
        // 在这里摘托盘图标或结束进程都会把 COM 工作站从回调栈上拆掉，
        // 进程随即在 combase 里崩成结束不掉的残骸。
        args.Cancel = true;
        DispatcherQueue.GetForCurrentThread().TryEnqueue(BeginShutdown);
    }

    /// <summary>
    /// 真正退出：摘掉托盘图标后结束进程。
    ///
    /// 用 TerminateProcess（Kill）而不是 Environment.Exit：后者会执行 DLL 拆卸与
    /// 托管收尾，WinRT/COM 对象在这个阶段被释放会抛 E_UNEXPECTED / RPC_E_WRONG_THREAD，
    /// 进程崩成需要重启才能清掉的残骸。本方法只做必然成功的事，没有可回滚的状态。
    /// </summary>
    private static void BeginShutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        try
        {
            Tray?.Dispose();
        }
        catch (Exception)
        {
            // 图标摘不掉不该拦住退出
        }

        Tray = null;
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>隐藏主窗口但保留进程与播放：托盘图标在隐藏期间保持可见。</summary>
    public static void HideToTray()
    {
        if (MainWindow is null)
        {
            return;
        }

        Tray?.SetVisible(true);

        // 图标没挂上就别藏：藏起来之后既看不到界面也点不到退出，等于留下一个关不掉的进程
        if (Tray is null || !Tray.IsVisible)
        {
            return;
        }

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

    /// <summary>
    /// 从托盘菜单退出：标记意图后按系统关闭消息关窗，收尾由 BeginShutdown 统一做。
    /// 菜单回调发生在托盘窗口的消息过程里，这里不再自己拆托盘对象。
    /// </summary>
    public static void ExitFromTray()
    {
        IsExiting = true;
        (MainWindow as MainWindow)?.RequestClose();
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
