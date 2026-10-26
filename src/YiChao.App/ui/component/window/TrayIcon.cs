using System;
using System.IO;
using System.Runtime.InteropServices;
using YiChao.Ui.Theme;

namespace YiChao.Ui.Component.Window;

/// <summary>
/// 通知区托盘图标：用 Win32 的 Shell_NotifyIcon 实现，不引入额外依赖。
///
/// 图标挂在一条只收消息的隐藏窗口上，左键恢复主界面，右键弹出「显示主界面 / 退出」。
/// 仅在关闭行为设为最小化到托盘时显示；退出菜单会先摘掉图标再走正常关窗流程。
/// 消息窗口建在 UI 线程，托盘回调因此和 WinUI 的布局更新同线程，无需再派发。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "YiChaoTrayWindow";

    private const uint TrayCallbackMessage = 0x8000 + 1; // WM_APP + 1

    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    private readonly Action _onShow;

    private readonly Action _onExit;

    private readonly WndProcDelegate _wndProc;

    private readonly IntPtr _moduleHandle;

    private IntPtr _windowHandle;

    private IntPtr _iconHandle;

    private bool _ownsIcon;

    private bool _visible;

    public TrayIcon(Action onShow, Action onExit)
    {
        _onShow = onShow;
        _onExit = onExit;
        _wndProc = WndProc;
        _moduleHandle = GetModuleHandle(null);

        try
        {
            RegisterWindowClass();
            _windowHandle = CreateWindowExW(
                0,
                WindowClassName,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                new IntPtr(-3), // HWND_MESSAGE：只收消息的隐藏窗口
                IntPtr.Zero,
                _moduleHandle,
                IntPtr.Zero);
        }
        catch (Exception)
        {
            // 托盘不可用不该拖垮主程序：后续 SetVisible 会因空句柄直接跳过
            _windowHandle = IntPtr.Zero;
        }
    }

    /// <summary>按关闭行为显示或移除托盘图标。</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        if (!visible)
        {
            RemoveIcon();
            _visible = false;
            return;
        }

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        AddIcon();
        _visible = true;
    }

    public void Dispose()
    {
        RemoveIcon();
        _visible = false;

        if (_iconHandle != IntPtr.Zero)
        {
            if (_ownsIcon)
            {
                DestroyIcon(_iconHandle);
            }

            _iconHandle = IntPtr.Zero;
        }

        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        UnregisterClassW(WindowClassName, _moduleHandle);
    }

    private void AddIcon()
    {
        _iconHandle = LoadTrayIcon();

        var data = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _iconHandle,
            szTip = Strings.TrayTooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private void RemoveIcon()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _windowHandle,
            uID = 1,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        Shell_NotifyIconW(NIM_DELETE, ref data);
    }

    private IntPtr LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            var handle = LoadImageW(
                IntPtr.Zero,
                iconPath,
                IMAGE_ICON,
                0,
                0,
                LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (handle != IntPtr.Zero)
            {
                _ownsIcon = true;
                return handle;
            }
        }

        _ownsIcon = false;
        return LoadIconW(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    private void RegisterWindowClass()
    {
        var windowClass = new WNDCLASSW
        {
            lpfnWndProc = _wndProc,
            hInstance = _moduleHandle,
            lpszClassName = WindowClassName,
        };

        RegisterClassW(ref windowClass);
    }

    private IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TrayCallbackMessage)
        {
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMessage)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    _onShow();
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        GetCursorPos(out var point);

        var menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING, 1, Strings.TrayShowWindow);
        AppendMenuW(menu, MF_SEPARATOR, 0, null);
        AppendMenuW(menu, MF_STRING, 2, Strings.TrayExit);

        // 菜单弹出前把隐藏窗口提到前台，否则点菜单外部不会关闭菜单
        SetForegroundWindow(_windowHandle);
        var command = TrackPopupMenu(
            menu,
            TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
            point.X,
            point.Y,
            0,
            _windowHandle,
            IntPtr.Zero);
        DestroyMenu(menu);
        PostMessageW(_windowHandle, 0, 0, IntPtr.Zero);

        if (command == 1)
        {
            _onShow();
        }
        else if (command == 2)
        {
            _onExit();
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASSW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(
        IntPtr instance,
        string name,
        uint type,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr hWnd,
        IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
}
