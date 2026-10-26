using System;
using System.Threading;

namespace YiChao.Ui.Component.Window;

/// <summary>
/// 单实例守卫：用命名互斥量判定首个实例，后续启动的进程通过命名事件请求唤醒已有窗口后自行退出。
///
/// 命名使用 Local\ 前缀，只在当前用户会话内生效，不同用户仍可各开一份。
/// 互斥量只以“是否存在”作为标记，不参与加锁等待，因此不需要在退出时显式释放。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\YiChao.App.Instance";

    private const string ActivateEventName = @"Local\YiChao.App.Activate";

    private static Mutex? _mutex;

    private static EventWaitHandle? _activateEvent;

    private static Thread? _listener;

    /// <summary>首个实例返回 true；已有实例时通知其唤醒窗口并返回 false。</summary>
    public static bool Acquire()
    {
        _mutex = new Mutex(false, MutexName, out var createdNew);
        if (!createdNew)
        {
            NotifyExisting();
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        return true;
    }

    /// <summary>
    /// 后台线程等待唤醒请求，收到后在 UI 线程执行激活回调。
    /// 监听线程设为后台线程，进程退出时无需额外收尾。
    /// </summary>
    public static void StartListening(Action activate)
    {
        if (_activateEvent is null || _listener is not null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
            {
                activate();
            }
        })
        {
            IsBackground = true,
            Name = "YiChao.ActivateListener",
        };
        _listener.Start();
    }

    /// <summary>请求已有实例显示窗口；事件尚未建好或已被回收时静默跳过。</summary>
    private static void NotifyExisting()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception)
        {
            // 唤醒失败只影响这次启动的焦点转移，不阻断新进程退出
        }
    }
}
