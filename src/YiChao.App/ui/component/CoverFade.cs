using System;

namespace YiChao.Ui.Component;

/// <summary>
/// 封面渐隐与歌词行渐隐共用的过渡曲线。
///
/// 曲线取 smootherstep 的补：两端一阶二阶导数为零，与带外的常值段平滑相接。
/// 线性渐隐只在折点处连续，斜率突变会被读成一条可辨的分界带。
/// </summary>
internal static class CoverFade
{
    /// <summary>封面羽化带长度占封面边长的比例，与忆潮竖屏封面下缘取同一值。</summary>
    public const double FadeRatio = 0.3;

    /// <summary>归一化位置对应的不透明度：由 1 平滑收敛到 0。</summary>
    public static double SmoothFadeAlpha(double t)
    {
        var x = Math.Clamp(t, 0, 1);
        return 1.0 - x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
    }
}
