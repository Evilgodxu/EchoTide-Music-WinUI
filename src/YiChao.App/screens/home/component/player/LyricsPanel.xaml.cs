using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
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
///
/// 上下边缘按行在视口里的实际位置逐行压暗，形成与忆潮蒙层一致的渐隐带；
/// 滚动按行位置做 400ms 缓动，跨度过大时直接定位。滚动与行缩放由同一条 60fps 时钟驱动并直接写属性，
/// 同一属性始终只有一个动画来源，不会因多段动画争抢而抖动。
///
/// 所有行一次性建好并整体平移，不按窗口虚拟化：切歌时重建一次，播放期只有一行在变（裁剪与两行的高亮切换），
/// 逐帧代价与行数无关。
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    /// <summary>上下渐隐带合计覆盖的行数：与忆潮的 FADE_TOTAL_LINES 一致，上下各半。</summary>
    private const double FadeTotalLines = 1.6;

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
    private double _fadeBand = 60;

    /// <summary>滚动与缩放共用一条 60fps 时钟，直接写属性值，不叠多段 Storyboard。</summary>
    private readonly DispatcherQueueTimer _animationTimer;

    private bool _scrollAnimating;
    private double _scrollFrom;
    private double _scrollTo;
    private long _scrollStartedAt;

    public LyricsPanel()
    {
        InitializeComponent();
        EmptyHint.Text = Strings.NoLyrics;
        EmptyHint.Visibility = Visibility.Collapsed;
        ContentOffset.Y = 0;
        SizeChanged += OnPanelSizeChanged;

        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animationTimer.IsRepeating = true;
        _animationTimer.Tick += (_, _) => OnAnimationTick();

        UpdateViewportMetrics();
    }

    public bool HasPositioned { get; private set; }

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
        StopAnimations();
        ContentOffset.Y = 0;

        EmptyHint.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var line in lines)
        {
            var row = new LyricRow(line, _fontSize, _lineHeight, _mainHeight, _rowSpacing);
            _rows.Add(row);
            LinesHost.Children.Add(row.Root);
        }

        ApplyRowMetrics();
        UpdateRowMetrics();
    }

    /// <summary>按播放位置推进：当前行变化时移窗，同帧内始终刷新扫光进度。</summary>
    public void UpdatePosition(TimeSpan position)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var milliseconds = position.TotalMilliseconds;
        var active = 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].TimeMs <= milliseconds)
            {
                active = i;
            }
            else
            {
                break;
            }
        }

        if (active != _activeIndex)
        {
            SetActive(active);
            HasPositioned = true;
        }

        UpdateSweep(active, milliseconds);
        // 滚动动画进行中行在视口里的位置每帧都在变，渐隐按当前位移重算
        UpdateRowOpacity();
    }

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
        current.SetTranslationActive(true);
        AnimateScale(current, ActiveScale);
        AnimateOffset(OffsetTarget(index));
        UpdateRowOpacity();
    }

    /// <summary>
    /// 上下边缘渐隐：按行在视口里的实际上下沿与渐隐带的比例压暗，
    /// 行越接近边缘越透明。与按行号距离淡出相比，行高不等（带译文）时也能贴住真实边缘。
    /// </summary>
    private void UpdateRowOpacity()
    {
        var band = Math.Max(1, _fadeBand);
        for (var i = 0; i < _rows.Count; i++)
        {
            var top = _rowTops[i] + ContentOffset.Y;
            var bottom = top + _rowHeights[i];
            var topFade = EdgeAlpha(top, band);
            var bottomFade = EdgeAlpha(_viewportHeight - bottom, band);
            var edge = Math.Clamp(Math.Min(topFade, bottomFade), 0, 1);
            _rows[i].Root.Opacity = (i == _activeIndex ? 1.0 : InactiveAlpha) * edge;
        }
    }

    /// <summary>距边缘越远越不透明：带内按 smootherstep 收敛，带外完全不透明。</summary>
    private static double EdgeAlpha(double distance, double band) =>
        1 - CoverFade.SmoothFadeAlpha(distance / band);

    private void UpdateSweep(int index, double milliseconds)
    {
        var row = _rows[index];
        var line = _lines[index];
        var nextTime = index + 1 < _lines.Count ? _lines[index + 1].TimeMs : line.TimeMs + LastLineFallbackDurationMs;
        var duration = Math.Max(1, nextTime - line.TimeMs);
        var length = Math.Max(1, line.Text.Length);
        var perChar = (double)duration / length;
        // 字符级进度折算为整行宽度的比例
        var fraction = Math.Clamp((milliseconds - line.TimeMs) / perChar / length, 0, 1);
        var width = row.Pending.ActualWidth;
        if (width <= 0 || fraction <= 0)
        {
            row.Fill.Clip = null;
            return;
        }

        row.FillClip.Rect = new Rect(0, 0, width * fraction, row.MainHeight);
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
        var snapDistance = ScrollSnapRows * (_mainHeight + _rowSpacing);
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
        _scrollStartedAt = Environment.TickCount64;
        _scrollAnimating = true;
        _animationTimer.Start();
    }

    private void StopScrollAnimation() => _scrollAnimating = false;

    private void AnimateScale(LyricRow row, double target)
    {
        row.ScaleFrom = row.Scale.ScaleX;
        row.ScaleTo = target;
        row.ScaleStartedAt = Environment.TickCount64;
        row.ScaleAnimating = true;
        _animationTimer.Start();
    }

    /// <summary>
    /// 60fps 时钟：滚动与行缩放都在这里按缓动曲线直接写属性，
    /// 同一属性始终只有一个动画来源，不会出现多段动画互相拉扯的抖动。
    /// </summary>
    private void OnAnimationTick()
    {
        var now = Environment.TickCount64;
        var running = false;

        if (_scrollAnimating)
        {
            var progress = Math.Clamp((now - _scrollStartedAt) / ScrollDurationMs, 0, 1);
            if (progress >= 1)
            {
                ContentOffset.Y = _scrollTo;
                _scrollAnimating = false;
            }
            else
            {
                ContentOffset.Y = _scrollFrom + ((_scrollTo - _scrollFrom) * EaseOutCubic(progress));
                running = true;
            }

            UpdateRowOpacity();
        }

        foreach (var row in _rows)
        {
            if (!row.ScaleAnimating)
            {
                continue;
            }

            var progress = Math.Clamp((now - row.ScaleStartedAt) / ScaleDurationMs, 0, 1);
            var scale = progress >= 1
                ? row.ScaleTo
                : row.ScaleFrom + ((row.ScaleTo - row.ScaleFrom) * EaseOutCubic(progress));
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
            _animationTimer.Stop();
        }
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>重建行或重排尺寸时停掉所有在途动画，下一帧从静态状态重新开始。</summary>
    private void StopAnimations()
    {
        _scrollAnimating = false;
        foreach (var row in _rows)
        {
            row.ScaleAnimating = false;
        }

        _animationTimer.Stop();
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
    /// </summary>
    private void UpdateViewportMetrics()
    {
        var layout = App.Settings.LyricLayout;
        var visibleLines = Math.Max(3, layout.VisibleLines);

        var fontSize = (double)layout.FontSize;
        var lineHeight = Math.Round(fontSize * 4 / 3);
        var mainHeight = lineHeight + 4;
        var spacing = (double)layout.LineSpacing;
        var requested = (mainHeight + spacing) * visibleLines - spacing;

        var availableHeight = _availableHeight > 0 ? _availableHeight : requested;
        var fit = Math.Clamp(availableHeight / Math.Max(1, requested), 0.6, 1.0);
        if (fit < 0.995)
        {
            fontSize = Math.Max(10, Math.Round(fontSize * fit));
            lineHeight = Math.Max(12, Math.Round(lineHeight * fit));
            mainHeight = lineHeight + Math.Max(2, Math.Round(4 * fit));
            spacing = Math.Round(spacing * fit);
        }

        _fontSize = fontSize;
        _lineHeight = lineHeight;
        _mainHeight = mainHeight;
        _rowSpacing = spacing;
        _translationFontSize = Math.Round(_fontSize * TranslationScale);
        _translationLineHeight = Math.Round(_lineHeight * TranslationScale);
        _translationGap = Math.Max(1, Math.Round(2 * fit));
        _visibleLines = visibleLines;

        var viewportHeight = ((mainHeight + spacing) * visibleLines) - spacing;
        _viewportHeight = Math.Min(availableHeight, Math.Max(mainHeight, viewportHeight));
        Viewport.Height = _viewportHeight;
        Viewport.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Math.Max(1, _availableWidth), _viewportHeight),
        };
        _fadeBand = Math.Max(1, _viewportHeight * FadeTotalLines / visibleLines);

        ApplyRowMetrics();
        UpdateRowMetrics();

        if (HasPositioned && _activeIndex >= 0 && _activeIndex < _rows.Count)
        {
            StopAnimations();
            ContentOffset.Y = OffsetTarget(_activeIndex);
        }

        UpdateRowOpacity();
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
                _translationGap);
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

            ApplyMetrics(fontSize, lineHeight, mainHeight, rowSpacing, fontSize * TranslationScale, lineHeight * TranslationScale, 2);
        }

        public Grid Root { get; }

        public TextBlock Pending { get; }

        public TextBlock Fill { get; }

        public TextBlock? Translation { get; }

        public ScaleTransform Scale { get; }

        public double ScaleFrom { get; set; }

        public double ScaleTo { get; set; }

        public long ScaleStartedAt { get; set; }

        public bool ScaleAnimating { get; set; }

        public RectangleGeometry FillClip { get; }

        public double MainHeight => _mainHost.Height;

        public double Height { get; private set; }

        public void ApplyMetrics(
            double fontSize,
            double lineHeight,
            double mainHeight,
            double rowSpacing,
            double translationFontSize,
            double translationLineHeight,
            double translationGap)
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

            Height = mainHeight + (Translation is null ? 0 : translationGap + translationLineHeight);
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
