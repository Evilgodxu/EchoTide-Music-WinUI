using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using YiChao.Ui.Component.Window;

namespace YiChao;

/// <summary>
/// 进程入口。单实例判定必须发生在 WinRT 初始化之前：后续启动的实例只负责唤醒已有窗口，
/// 这里直接返回即可干净结束进程。
///
/// 若把这段判断放进 App 构造函数，退出就发生在 Application.Start 的回调栈上，
/// COM 工作站会在回调内部被拆掉（combase 抛 E_UNEXPECTED），进程随之崩成
/// 结束不掉的残骸——这正是"关掉后还留一个僵尸进程"的来源。
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!SingleInstance.Acquire())
        {
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
