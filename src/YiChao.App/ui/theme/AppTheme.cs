using System;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;
using YiChao.Data.Settings;

namespace YiChao.Ui.Theme;

/// <summary>
/// 主题：把「跟随系统 / 浅色 / 深色」解析成一组实际颜色，并统一下发。
///
/// 首页不受主题影响——那一片的颜色来自封面，前景恒为白色（与忆潮一致），
/// 主题只作用于设置页这类纯色页面与窗口标题按钮。
/// 颜色取自忆潮的两套配色：深色是 GitHub Dark，浅色是同源浅色档。
/// </summary>
public static class AppTheme
{
    public static event Action? Changed;

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    public static bool IsDark { get; private set; } = true;

    public static Color Background => IsDark ? DarkBackground : LightBackground;

    public static Color Surface => IsDark ? DarkSurface : LightSurface;

    public static Color OnSurface => IsDark ? DarkOnSurface : LightOnSurface;

    public static Color OnSurfaceVariant => IsDark ? DarkOnSurfaceVariant : LightOnSurfaceVariant;

    private static readonly Color DarkBackground = Color.FromArgb(0xFF, 0x0D, 0x11, 0x17);
    private static readonly Color DarkSurface = Color.FromArgb(0xFF, 0x16, 0x1B, 0x22);
    private static readonly Color DarkOnSurface = Color.FromArgb(0xFF, 0xF0, 0xF6, 0xFC);
    private static readonly Color DarkOnSurfaceVariant = Color.FromArgb(0xFF, 0x8B, 0x94, 0x9E);

    private static readonly Color LightBackground = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color LightSurface = Color.FromArgb(0xFF, 0xF6, 0xF8, 0xFA);
    private static readonly Color LightOnSurface = Color.FromArgb(0xFF, 0x1F, 0x23, 0x28);
    private static readonly Color LightOnSurfaceVariant = Color.FromArgb(0xFF, 0x65, 0x6D, 0x76);

    public static void Initialize(ThemeMode mode)
    {
        Mode = mode;
        IsDark = ResolveDark(mode);
    }

    public static void Set(ThemeMode mode)
    {
        Mode = mode;
        IsDark = ResolveDark(mode);
        Apply();
        Changed?.Invoke();
    }

    /// <summary>把当前主题下发到根元素与窗口标题按钮。</summary>
    public static void Apply()
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = Mode switch
            {
                ThemeMode.Light => ElementTheme.Light,
                ThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        (App.MainWindow as MainWindow)?.ApplyTitleBarColors(IsDark);
    }

    /// <summary>系统主题：用系统背景色判断深浅，比读注册表稳。</summary>
    private static bool ResolveDark(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => IsSystemDark(),
    };

    private static bool IsSystemDark()
    {
        try
        {
            var color = new UISettings().GetColorValue(UIColorType.Background);
            return (color.R + color.G + color.B) / 3 < 128;
        }
        catch (Exception)
        {
            // 取不到系统主题时按深色处理：这是本应用的默认外观
            return true;
        }
    }
}
