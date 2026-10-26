namespace YiChao.Data.Settings;

/// <summary>主题模式：跟随系统、浅色、深色。与忆潮的三档一致。</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>界面语言：跟随系统、简体中文、English。</summary>
public enum AppLanguage
{
    System,
    ChineseSimplified,
    English,
}

/// <summary>关闭按钮行为：直接结束进程，或缩到通知区托盘继续播放。</summary>
public enum CloseBehavior
{
    Exit,
    MinimizeToTray,
}

/// <summary>
/// 歌词排版参数：字号、可见行数与行间距。取值与忆潮横屏歌词排版一致。
///
/// 可见行数取奇数预设，当前行才能上下对称居中；字号与行间距按整数步进调节。
/// </summary>
public sealed class LyricLayoutSettings
{
    public const int MinFontSize = 12;

    public const int MaxFontSize = 24;

    public const int DefaultFontSize = 18;

    public const int DefaultVisibleLines = 11;

    public const int MinLineSpacing = 0;

    public const int MaxLineSpacing = 18;

    public const int DefaultLineSpacing = 10;

    /// <summary>横屏可见行数预设：7/9/11，与忆潮保持一致。</summary>
    public static readonly int[] VisibleLinePresets = { 7, 9, 11 };

    public int FontSize { get; set; } = DefaultFontSize;

    public int VisibleLines { get; set; } = DefaultVisibleLines;

    public int LineSpacing { get; set; } = DefaultLineSpacing;

    /// <summary>夹到合法范围，并把行数吸附到最近的奇数预设。</summary>
    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, MinFontSize, MaxFontSize);
        LineSpacing = Math.Clamp(LineSpacing, MinLineSpacing, MaxLineSpacing);

        var nearest = VisibleLinePresets[0];
        var best = int.MaxValue;
        foreach (var preset in VisibleLinePresets)
        {
            var distance = Math.Abs(preset - VisibleLines);
            if (distance < best)
            {
                best = distance;
                nearest = preset;
            }
        }

        VisibleLines = nearest;
    }

    public LyricLayoutSettings Clone() => new()
    {
        FontSize = FontSize,
        VisibleLines = VisibleLines,
        LineSpacing = LineSpacing,
    };
}
