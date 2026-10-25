using Windows.UI;

namespace YiChao.Ui.Theme;

/// <summary>
/// 忆潮深色配色，取值与 Android 端 <c>theme/Color.kt</c> 一一对应，不做主题切换。
/// </summary>
internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(0xFF, 0x0D, 0x11, 0x17);
    public static readonly Color Surface = Color.FromArgb(0xFF, 0x16, 0x1B, 0x22);
    public static readonly Color SurfaceVariant = Color.FromArgb(0xFF, 0x21, 0x26, 0x2D);

    /// <summary>前景文字与图标：首页统一纯白，层次由不透明度分出。</summary>
    public static readonly Color Foreground = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>未唱歌词与次级文字的不透明度。</summary>
    public const double PendingTextAlpha = 0.72;
}
