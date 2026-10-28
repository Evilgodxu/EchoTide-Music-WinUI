using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using YiChao.Data.Lyrics;
using YiChao.Data.Settings;
using YiChao.Ui.Theme;
using YiChao.Ui.Component;

namespace YiChao.Screens.Home.Component.Player;

/// <summary>
/// 横屏歌词面板：当前行居中、上下对称排开，整列随播放进度平移，当前行按字序点亮并放大。
///
/// 逐字点亮用两层同排版文本叠放：底层整行按未唱色铺满，上层同色更高的文本按进度矩形裁剪，
/// 裁剪宽度随播放位置推进，读起来就是一个从左到右的扫光。WinUI 没有逐字文本布局，
/// 用双层裁剪代替逐字定位——观感一致，代价是没有忆潮那种逐字的弹簧跳动。
///
/// 排版参数（字号、可见行数、行间距）来自设置页，与忆潮横屏歌词排版同口径：默认 18 号、11 行、10 间距，
/// 窗口高度不足时整体等比收缩，行数保持设定值，不再按窗口大小自动塞入更多行。
/// 视口高度按整行（含译文槽位）折算，带译文的曲目同样能看到设定行数。
///
/// 上下边缘各按半行做渐隐：只把最外沿那行压暗一部分，不吞掉整行；
/// 滚动按行位置做 400ms 缓动，跨度过大时直接定位。滚动、缩放与扫光共用同一条显示帧时钟并直接写属性，
/// 同一属性始终只有一个动画来源，不会因多段动画争抢而抖动。
///
/// 时钟跟着屏幕刷新率走：CompositionTarget.Rendering 每呈现一帧回调一次，60/120/165/180Hz 都按各自帧率推进，
/// 不写死帧数。进度一律按真实时间折算（Stopwatch 计时），因此帧率只决定画面采样密度，不影响动画时长与缓动曲线。
/// 位置采样只用来校准：扫光在帧间按行时长线性插值推进，采样频率低也不会出现一格一格的台阶。
/// 没有动画在跑时立刻摘掉帧回调——挂着它会让 UI 线程每帧都被唤醒，白白耗电。
///
/// 所有行一次性建好并整体平移，不按窗口虚拟化：切歌时重建一次，播放期只有一行在变（裁剪与两行的高亮切换），
/// 逐帧代价与行数无关。
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    /// <summary>视口上下每侧渐隐带覆盖的行数：只压暗最外沿半行，不吞掉整行。</summary>
    private const double FadeLinesPerEdge = 0.5;

    /// <summary>滚动缓动时长与忆潮一致。</summary>
    private const double ScrollDurationMs = 400;

    /// <summary>行放大/回落的过渡时长。</summary>
    private const double ScaleDurationMs = 260;

    /// <summary>跨度超过两行时直接定位，避免长距离滚动拖沓。</summary>
    private const double ScrollSnapRows = 2;

    private const double InactiveAlpha = 0.55;
    private const double InactiveScale = 0.98;
    private const double ActiveScale = 1.14;
    private const double PendingAlpha = 0.72;
    private const double TranslationScale = 0.68;

    /// <summary>末行没有下一行时间戳时按 3 秒点亮完毕，与忆潮一致。</summary>
    private const long LastLineFallbackDurationMs = 3000;

    /// <summary>
    /// 扫光与位置采样偏差超过这个时长才拉回。采样本身有几十毫秒的延迟与量化，
    /// 阈值取一个采样周期：正常播放时插值自洽不会被反复纠偏，拖动进度造成的真实跳变又会立刻跟上。
    /// </summary>
    private const double SweepResyncToleranceMs = 50;

    /// <summary>行透明度小于此差值不重写，渐隐带以外的行因此整帧不动。</summary>
    private const double OpacityWriteEpsilon = 0.004;

    private static readonly Color PendingColor = Color.FromArgb((byte)Math.Round(PendingAlpha * 255), 0xFF, 0xFF, 0xFF);
    private static readonly Color ActiveColor = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
    private static readonly Color TranslationColor = Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF);

    private static readonly SolidColorBrush ActiveTranslationBrush = new(ActiveColor);

    private static readonly SolidColorBrush InactiveTranslationBrush = new(TranslationColor);

    private readonly List<LyricRow> _rows = new();

    /// <summary>每行在内容坐标系里的顶端与高度，含译文行；滚动定位与居中都按它算。</summary>
    private readonly List<double> _rowTops = new();

    private readonly List<double> _rowHeights = new();

    private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
    private int _activeIndex = -1;

    private double _availableWidth = 576;
    private double _availableHeight;
    private double _fontSize = LyricLayoutSettings.DefaultFontSize;
    private double _lineHeight = 24;
    private double _mainHeight = 28;
    private double _rowSpacing = LyricLayoutSettings.DefaultLineSpacing;
    private double _translationFontSize = 12;
    private double _translationLineHeight = 16;
    private double _translationGap = 2;
    private int _visibleLines = LyricLayoutSettings.DefaultVisibleLines;
    private double _viewportHeight = 408;

    /// <summary>一行歌词占用的完整高度（含译文与行间距），决定视口尺寸与渐隐带宽度。</summary>
    private double _rowPitch = 38;

    /// <summary>曲目带译文时所有行统一预留译文高度，保证可见行数不随译文有无而漂移。</summary>
    private bool _reserveTranslationHeight;

    /// <summary>滚动、缩放与扫光共用一条显示帧时钟，直接写属性值，不叠多段 Storyboard。</summary>
    private bool _frameHooked;

    private bool _scrollAnimating;
    private double _scrollFrom;
    private double _scrollTo;
    private double _scrollStartedAt;

    /// <summary>扫光：从锚点比例按行时长线性推进，位置采样只负责校准。</summary>
    private bool _sweepAnimating;
    private double _sweepFrom;
    private double _sweepStartedAt;
    private double _sweepDuration;

    /// <summary>已写入的裁剪宽度；亚像素级变化不重写，省掉无意义的整行重绘。</summary>
    private double _sweepWidth = -1;

    /// <summary>播放态：暂停时扫光不随帧推进，位置采样直接落位。</summary>
    private bool _playing;

    public LyricsPanel()
    {
        InitializeComponent();
        EmptyHint.Text = Strings.NoLyrics;
        EmptyHint.Visibility = Visibility.Collapsed;
        ContentOffset.Y = 0;
        SizeChanged += OnPanelSizeChanged;
        Unloaded += (_, _) => StopFrameClock();

        UpdateViewportMetrics();
    }

    public bool HasPositioned { get; private set; }

    /// <summary>
    /// 动画时间轴。Environment.TickCount64 的分辨率只有约 15.6ms，
    /// 拿它算 400ms 缓动会把曲线量化成二十来个台阶——高刷屏上正是这些台阶看起来像掉帧；
    /// Stopwatch 走 QPC，微秒级，帧间差值是真实的。
    /// </summary>
    private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// 挂上显示帧时钟。只在有动画在跑时订阅：Rendering 一旦挂着就会逼 UI 线程每帧开工，
    /// 闲时立刻摘掉，否则一直在替一帧不动的画面耗电。
    /// </summary>
    private void StartFrameClock()
    {
        if (_frameHooked)
        {
            return;
        }

        CompositionTarget.Rendering += OnFrame;
        _frameHooked = true;
    }

    private void StopFrameClock()
    {
        if (!_frameHooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnFrame;
        _frameHooked = false;
    }

    /// <summary>播放/暂停：暂停后扫光停在原处，恢复时从当前位置接着走。</summary>
    public void SetPlaying(bool playing)
    {
        if (_playing == playing)
        {
            return;
        }

        _playing = playing;
        if (playing && _sweepAnimating)
        {
            StartFrameClock();
        }
    }

    /// <summary>语言切换后重取占位文案。</summary>
    public void ApplyLanguage() => EmptyHint.Text = Strings.NoLyrics;

    /// <summary>设置页改动歌词排版后重新量一次：字号、行数与间距都回到用户选定的值。</summary>
    public void ApplyLayoutSettings() => UpdateViewportMetrics();

    public void SetLines(IReadOnlyList<LyricLine> lines)
    {
        _lines = lines;
        _rows.Clear();
        _rowTops.Clear();
        _rowHeights.Clear();
        LinesHost.Children.Clear();
        _activeIndex = -1;
        HasPositioned = false;
        _sweepWidth = -1;
        // 锚点清零：换歌后第一帧必须按新曲目的位置重新落位，不能沿用上一首的比例
        _sweepDuration = 0;
        _sweepFrom = 0;
        StopAnimations();
        ContentOffset.Y = 0;

        EmptyHint.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var line in lines)
        {
            var row = new LyricRow(line, _fontSize, _lineHeight, _mainHeight, _rowSpacing);
            _rows.Add(row);
            LinesHost.Children.Add(row.Root);
        }

        // 译文的有无会改变整行高度，进而改变视口能容纳的行数，建行后重算一次
        UpdateViewportMetrics();
    }

    /// <summary>按播放位置推进：当前行变化时移窗，其余帧由显示帧时钟继续扫光。</summary>
    public void UpdatePosition(TimeSpan position)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var milliseconds = position.TotalMilliseconds;
        var active = ActiveLineAt(milliseconds);
        if (active != _activeIndex)
        {
            SetActive(active);
            HasPositioned = true;
        }

        SyncSweep(milliseconds);
    }

    /// <summary>二分找当前行：整首歌的位置更新都走这里，行数上千时也不必线性扫。</summary>
    private int ActiveLineAt(double milliseconds)
    {
        var low = 0;
        var high = _lines.Count - 1;
        var found = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_lines[mid].TimeMs <= milliseconds)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Math.Min(found, _rows.Count - 1);
    }

    /// <summary>
    /// 用位置采样校准扫光。采样是 20Hz 的，帧间由时钟按行时长线性推进；
    /// 偏差在容差内就不动，逐次硬对齐反而会把采样抖动翻译成扫光的顿挫。
    /// </summary>
    private void SyncSweep(double positionMs)
    {
        if (_activeIndex < 0 || _activeIndex >= _rows.Count)
        {
            return;
        }

        var line = _lines[_activeIndex];
        var next = _activeIndex + 1 < _lines.Count
            ? _lines[_activeIndex + 1].TimeMs
            : line.TimeMs + LastLineFallbackDurationMs;
        var duration = Math.Max(1.0, next - line.TimeMs);
        var fraction = Math.Clamp((positionMs - line.TimeMs) / duration, 0, 1);
        var now = NowMs();

        // 没有锚点（刚切歌或刚建行）时无从比较偏差，直接按采样落位
        var anchored = _sweepDuration > 0;
        var drift = anchored ? Math.Abs(SweepFractionAt(now) - fraction) * duration : 0;
        if (!anchored || !_playing || drift > SweepResyncToleranceMs)
        {
            _sweepFrom = fraction;
            _sweepStartedAt = now;
            _sweepDuration = duration;
            WriteSweep(fraction);
        }

        _sweepAnimating = _playing && fraction < 1;
        if (_sweepAnimating)
        {
            StartFrameClock();
        }
    }

    /// <summary>当前时刻按插值应有的扫光比例；尚未锚定时就是锚点值。</summary>
    private double SweepFractionAt(double now) =>
        _sweepDuration <= 0 ? _sweepFrom : _sweepFrom + ((now - _sweepStartedAt) / _sweepDuration);

    private void SetActive(int index)
    {
        if (_activeIndex >= 0 && _activeIndex < _rows.Count)
        {
            var previous = _rows[_activeIndex];
            previous.Fill.Clip = null;
            previous.SetTranslationActive(false);
            AnimateScale(previous, InactiveScale);
        }

        _activeIndex = index;
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        var current = _rows[index];
        // 换了行就重置已写宽度：新行哪怕宽度恰好相同也必须重写一次裁剪
        _sweepWidth = -1;
        current.SetTranslationActive(true);
        AnimateScale(current, ActiveScale);
        AnimateOffset(OffsetTarget(index));
        UpdateRowOpacity();
    }

    /// <summary>
    /// 上下边缘渐隐：渐隐带只有半行，按行落在带内的比例取平均透明度。
    /// 整行取一个透明度，若按行的最外沿算，正好贴住视口边缘的那一行会被算成完全透明而消失；
    /// 取平均则只把它压暗一部分，设定行数仍能在边缘看到。
    ///
    /// 每帧都对全部行算一遍，但只有落在渐隐带里的那两三行会真的改变透明度；
    /// 值没变就不写，省掉其余行的属性写入与随之而来的重绘。
    /// </summary>
    private void UpdateRowOpacity()
    {
        var band = Math.Max(1, FadeLinesPerEdge * _rowPitch);
        for (var i = 0; i < _rows.Count; i++)
        {
            var alpha = (i == _activeIndex ? 1.0 : InactiveAlpha) * RowEdgeAlpha(i, band);
            var row = _rows[i].Root;
            if (Math.Abs(alpha - row.Opacity) < OpacityWriteEpsilon)
            {
                continue;
            }

            row.Opacity = alpha;
        }
    }

    /// <summary>一行在视口上下两条渐隐带内的综合透明度，取两侧中更暗的一侧。</summary>
    private double RowEdgeAlpha(int index, double band)
    {
        var top = _rowTops[index] + ContentOffset.Y;
        var bottom = top + _rowHeights[index];
        return Math.Min(
            FadeAlphaOverRow(top, bottom, band),
            FadeAlphaOverRow(_viewportHeight - bottom, _viewportHeight - top, band));
    }

    /// <summary>
    /// 一行在渐隐带内的平均不透明度。near/far 是行的上下沿到视口边缘的距离，
    /// 对 smootherstep 求积得到整行均值，避免贴边行被一刀切成透明。
    /// </summary>
    private static double FadeAlphaOverRow(double near, double far, double band)
    {
        if (far <= 0)
        {
            return 0;
        }

        near = Math.Max(0, near);
        if (near >= band)
        {
            return 1;
        }

        var span = far - near;
        if (span <= 0)
        {
            return 1 - CoverFade.SmoothFadeAlpha(near / band);
        }

        var nearIntegral = FadeIntegral(near / band);
        if (far <= band)
        {
            return (band / span) * (FadeIntegral(far / band) - nearIntegral);
        }

        // 带内部分取积分，带外部分恒为 1
        return ((band / span) * (0.5 - nearIntegral)) + ((far - band) / span);
    }

    /// <summary>smootherstep 渐隐曲线 1 - S(x) 在 [0, x] 上的积分。</summary>
    private static double FadeIntegral(double x)
    {
        var t = Math.Clamp(x, 0, 1);
        return t - (t * t * t * t * t * t) + (3 * t * t * t * t * t) - (2.5 * t * t * t * t);
    }

    /// <summary>
    /// 扫光进度落到裁剪宽度上。比例由时钟插值给出，与行数、位置采样频率无关；
    /// 宽度变化不足半像素时不重写，慢速行里的重复写入就此省下。
    /// </summary>
    private void WriteSweep(double fraction)
    {
        var row = _rows[_activeIndex];
        var width = row.Pending.ActualWidth;
        if (width <= 0 || fraction <= 0)
        {
            _sweepWidth = 0;
            row.Fill.Clip = null;
            return;
        }

        var target = width * Math.Clamp(fraction, 0, 1);
        if (Math.Abs(target - _sweepWidth) < 0.5)
        {
            return;
        }

        _sweepWidth = target;
        row.FillClip.Rect = new Rect(0, 0, target, row.MainHeight);
        row.Fill.Clip = row.FillClip;
    }

    private void AnimateOffset(double target)
    {
        if (!HasPositioned)
        {
            StopScrollAnimation();
            // 首帧直接落位：从第一行开始播放时不该看见整列从顶部滚下来
            ContentOffset.Y = target;
            return;
        }

        var distance = Math.Abs(target - ContentOffset.Y);
        var snapDistance = ScrollSnapRows * _rowPitch;
        if (distance > snapDistance || distance < 0.5)
        {
            // 长跨度（拖动进度、切歌）直接定位，避免整列长时间滚动
            StopScrollAnimation();
            ContentOffset.Y = target;
            UpdateRowOpacity();
            return;
        }

        // 当前值即动画起点：上一次滚动若未结束，从此刻实际位置接着走，不回弹
        _scrollFrom = ContentOffset.Y;
        _scrollTo = target;
        _scrollStartedAt = NowMs();
        _scrollAnimating = true;
        StartFrameClock();
    }

    private void StopScrollAnimation() => _scrollAnimating = false;

    private void AnimateScale(LyricRow row, double target)
    {
        row.ScaleFrom = row.Scale.ScaleX;
        row.ScaleTo = target;
        row.ScaleStartedAt = NowMs();
        row.ScaleAnimating = true;
        StartFrameClock();
    }

    /// <summary>
    /// 逐帧推进：回调频率即屏幕刷新率，所有进度按真实时间折算，
    /// 因此 60Hz 与 180Hz 走的是同一条曲线、同样时长，只是采样更密。
    /// 没有动画在跑就让时钟自己停下，别留一个空转的帧回调。
    /// </summary>
    private void OnFrame(object? sender, object e)
    {
        var now = NowMs();
        var running = false;

        if (_scrollAnimating)
        {
            var progress = Math.Clamp((now - _scrollStartedAt) / ScrollDurationMs, 0, 1);
            ContentOffset.Y = _scrollFrom + ((_scrollTo - _scrollFrom) * EaseOutCubic(progress));
            UpdateRowOpacity();

            if (progress >= 1)
            {
                _scrollAnimating = false;
            }
            else
            {
                running = true;
            }
        }

        if (_sweepAnimating && _playing)
        {
            var fraction = SweepFractionAt(now);
            WriteSweep(fraction);
            if (fraction >= 1)
            {
                _sweepAnimating = false;
            }
            else
            {
                running = true;
            }
        }

        foreach (var row in _rows)
        {
            if (!row.ScaleAnimating)
            {
                continue;
            }

            var progress = Math.Clamp((now - row.ScaleStartedAt) / ScaleDurationMs, 0, 1);
            var scale = row.ScaleFrom + ((row.ScaleTo - row.ScaleFrom) * EaseOutCubic(progress));
            row.Scale.ScaleX = scale;
            row.Scale.ScaleY = scale;

            if (progress >= 1)
            {
                row.ScaleAnimating = false;
            }
            else
            {
                running = true;
            }
        }

        if (!running)
        {
            StopFrameClock();
        }
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>重建行或重排尺寸时停掉所有在途动画，下一帧从静态状态重新开始。</summary>
    private void StopAnimations()
    {
        _scrollAnimating = false;
        _sweepAnimating = false;
        foreach (var row in _rows)
        {
            row.ScaleAnimating = false;
        }

        StopFrameClock();
    }

    /// <summary>
    /// 面板尺寸变化：只把可用宽高记下来重排，不改动用户设定的字号与行数。
    /// 尺寸变化期间直接落位，不播位移动画。
    /// </summary>
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        _availableWidth = e.NewSize.Width;
        _availableHeight = e.NewSize.Height;
        UpdateViewportMetrics();
    }

    /// <summary>
    /// 按设置计算视口与每行样式：设定行数决定视口高度，窗口放不下时整体等比收缩；
    /// 窗口更大也不再增加行数，避免歌词密密麻麻占满整屏。
    /// 行高按整行（含译文）计算，否则带译文的曲目会因每行变高而少显示好几行。
    /// </summary>
    private void UpdateViewportMetrics()
    {
        var layout = App.Settings.LyricLayout;
        var visibleLines = Math.Max(3, layout.VisibleLines);
        var reserveTranslation = HasTranslationLines();

        var fontSize = (double)layout.FontSize;
        var lineHeight = Math.Round(fontSize * 4 / 3);
        var mainHeight = lineHeight + 4;
        var spacing = (double)layout.LineSpacing;
        var translationFontSize = Math.Round(fontSize * TranslationScale);
        var translationLineHeight = Math.Round(lineHeight * TranslationScale);
        var translationGap = 2.0;

        double RowPitch() =>
            mainHeight + (reserveTranslation ? translationGap + translationLineHeight : 0) + spacing;

        var requested = (RowPitch() * visibleLines) - spacing;

        var availableHeight = _availableHeight > 0 ? _availableHeight : requested;
        var fit = Math.Clamp(availableHeight / Math.Max(1, requested), 0.6, 1.0);
        if (fit < 0.995)
        {
            fontSize = Math.Max(10, Math.Round(fontSize * fit));
            lineHeight = Math.Max(12, Math.Round(lineHeight * fit));
            mainHeight = lineHeight + Math.Max(2, Math.Round(4 * fit));
            spacing = Math.Round(spacing * fit);
            translationFontSize = Math.Round(fontSize * TranslationScale);
            translationLineHeight = Math.Round(lineHeight * TranslationScale);
            translationGap = Math.Max(1, Math.Round(2 * fit));
        }

        _fontSize = fontSize;
        _lineHeight = lineHeight;
        _mainHeight = mainHeight;
        _rowSpacing = spacing;
        _translationFontSize = translationFontSize;
        _translationLineHeight = translationLineHeight;
        _translationGap = translationGap;
        _visibleLines = visibleLines;
        _reserveTranslationHeight = reserveTranslation;
        _rowPitch = RowPitch();

        var viewportHeight = (_rowPitch * visibleLines) - spacing;
        _viewportHeight = Math.Min(availableHeight, Math.Max(mainHeight, viewportHeight));
        Viewport.Height = _viewportHeight;
        Viewport.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Math.Max(1, _availableWidth), _viewportHeight),
        };

        ApplyRowMetrics();
        UpdateRowMetrics();

        if (HasPositioned && _activeIndex >= 0 && _activeIndex < _rows.Count)
        {
            StopAnimations();
            ContentOffset.Y = OffsetTarget(_activeIndex);
        }

        UpdateRowOpacity();
    }

    /// <summary>曲目是否带译文；带译文时整行高度增加，可见行数需按加高后的行距折算。</summary>
    private bool HasTranslationLines()
    {
        foreach (var line in _lines)
        {
            if (!string.IsNullOrWhiteSpace(line.Translation))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyRowMetrics()
    {
        foreach (var row in _rows)
        {
            row.ApplyMetrics(
                _fontSize,
                _lineHeight,
                _mainHeight,
                _rowSpacing,
                _translationFontSize,
                _translationLineHeight,
                _translationGap,
                _reserveTranslationHeight);
        }
    }

    /// <summary>累计每行顶端与总高，供居中定位使用。</summary>
    private void UpdateRowMetrics()
    {
        _rowTops.Clear();
        _rowHeights.Clear();

        var top = 0.0;
        foreach (var row in _rows)
        {
            _rowTops.Add(top);
            _rowHeights.Add(row.Height);
            top += row.Height + _rowSpacing;
        }
    }

    private double OffsetTarget(int index)
    {
        if (index < 0 || index >= _rowTops.Count)
        {
            return ContentOffset.Y;
        }

        var center = _rowTops[index] + (_rowHeights[index] / 2);
        return (_viewportHeight / 2) - center;
    }

    /// <summary>一行歌词：主行两层同排版文本 + 可选译文行 + 各自的高亮态变换。</summary>
    private sealed class LyricRow
    {
        private readonly Grid _mainHost;

        public LyricRow(
            LyricLine line,
            double fontSize,
            double lineHeight,
            double mainHeight,
            double rowSpacing)
        {
            Scale = new ScaleTransform { ScaleX = InactiveScale, ScaleY = InactiveScale };
            FillClip = new RectangleGeometry();

            Pending = CreateText(line.Text, PendingColor, fontSize, lineHeight);
            Fill = CreateText(line.Text, ActiveColor, fontSize, lineHeight);
            Fill.Clip = null;

            _mainHost = new Grid { Height = mainHeight };
            _mainHost.Children.Add(Pending);
            _mainHost.Children.Add(Fill);

            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(_mainHost);

            var translation = line.Translation?.Trim();
            if (!string.IsNullOrEmpty(translation))
            {
                Translation = CreateText(translation, TranslationColor, fontSize * TranslationScale, lineHeight * TranslationScale);
                content.Children.Add(Translation);
            }

            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Opacity = InactiveAlpha,
                RenderTransform = Scale,
                // 缩放围绕行中心，放大后仍与中线对齐
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Root.Children.Add(content);

            ApplyMetrics(fontSize, lineHeight, mainHeight, rowSpacing, fontSize * TranslationScale, lineHeight * TranslationScale, 2, false);
        }

        public Grid Root { get; }

        public TextBlock Pending { get; }

        public TextBlock Fill { get; }

        public TextBlock? Translation { get; }

        public ScaleTransform Scale { get; }

        public double ScaleFrom { get; set; }

        public double ScaleTo { get; set; }

        public double ScaleStartedAt { get; set; }

        public bool ScaleAnimating { get; set; }

        public RectangleGeometry FillClip { get; }

        public double MainHeight => _mainHost.Height;

        public double Height { get; private set; }

        /// <summary>
        /// 应用排版参数。reserveTranslationHeight 为真时，即使本行没有译文也留出译文槽位，
        /// 让同一首曲目的每行等高，可见行数才不会因译文疏密而忽多忽少。
        /// </summary>
        public void ApplyMetrics(
            double fontSize,
            double lineHeight,
            double mainHeight,
            double rowSpacing,
            double translationFontSize,
            double translationLineHeight,
            double translationGap,
            bool reserveTranslationHeight)
        {
            Pending.FontSize = fontSize;
            Pending.LineHeight = lineHeight;
            Fill.FontSize = fontSize;
            Fill.LineHeight = lineHeight;
            _mainHost.Height = mainHeight;

            if (Translation is not null)
            {
                Translation.FontSize = translationFontSize;
                Translation.LineHeight = translationLineHeight;
                Translation.Margin = new Thickness(0, translationGap, 0, 0);
            }

            Height = mainHeight + (Translation is not null || reserveTranslationHeight ? translationGap + translationLineHeight : 0);
            Root.Height = Height;
            Root.Margin = new Thickness(0, 0, 0, rowSpacing);
        }

        /// <summary>当前行的译文与主行同步提亮；其余行回到暗一档的译文色。</summary>
        public void SetTranslationActive(bool active)
        {
            if (Translation is not null)
            {
                Translation.Foreground = active ? ActiveTranslationBrush : InactiveTranslationBrush;
            }
        }

        private static TextBlock CreateText(string text, Color color, double fontSize, double lineHeight) => new()
        {
            Text = text,
            FontSize = fontSize,
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
    }
}
