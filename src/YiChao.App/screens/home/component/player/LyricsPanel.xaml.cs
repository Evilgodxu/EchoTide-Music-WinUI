using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using YiChao.Data.Lyrics;
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
/// 行高、字号、可见行数与边缘淡出范围都按面板实际尺寸换算：窗口放大或缩小时重排一次，
/// 不同窗口尺寸下看到的行数与字号随之变化，不会出现小窗口被裁掉、大窗口只占中间一条。
/// 有译文的行在主行下方再排一行小字，行高随之增加，排版与滚动定位都按实际行高累计。
///
/// 所有行一次性建好并整体平移，不按窗口虚拟化：切歌时重建一次，播放期只有一行在变（裁剪与两行的高亮切换），
/// 逐帧代价与行数无关。
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    /// <summary>基准字号：对应 1200×780 窗口下右栏的可用区域。</summary>
    private const double BaseFontSize = 18;

    private const double BaseLineHeight = 24;

    private const double BaseRowSpacing = 10;

    /// <summary>基准可用区域：窗口 1200×780 去掉右栏内边距后的尺寸。</summary>
    private const double BaseViewportWidth = 576;

    private const double BaseViewportHeight = 756;

    /// <summary>缩放上下限：窗口再小也要保住可读字号，再大也不把歌词撑成标题。</summary>
    private const double MinScale = 0.72;

    private const double MaxScale = 1.5;

    private const double InactiveAlpha = 0.55;
    private const double InactiveScale = 0.98;
    private const double ActiveScale = 1.14;
    private const double PendingAlpha = 0.72;

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

    private double _fontSize = BaseFontSize;
    private double _lineHeight = BaseLineHeight;
    private double _mainHeight = BaseLineHeight + 4;
    private double _rowSpacing = BaseRowSpacing;
    private double _translationFontSize = 12;
    private double _translationLineHeight = 16;
    private double _translationGap = 2;
    private double _scale = 1;
    private double _viewportHeight = BaseViewportHeight;
    private double _fadeStartRows = 3.4;
    private double _fadeEndRows = 5.5;

    public LyricsPanel()
    {
        InitializeComponent();
        Viewport.Height = _viewportHeight;
        EmptyHint.Text = Strings.NoLyrics;
        EmptyHint.Visibility = Visibility.Collapsed;
        ContentOffset.Y = 0;
        SizeChanged += OnPanelSizeChanged;
    }

    public bool HasPositioned { get; private set; }

    /// <summary>语言切换后重取占位文案。</summary>
    public void ApplyLanguage() => EmptyHint.Text = Strings.NoLyrics;

    public void SetLines(IReadOnlyList<LyricLine> lines)
    {
        _lines = lines;
        _rows.Clear();
        _rowTops.Clear();
        _rowHeights.Clear();
        LinesHost.Children.Clear();
        _activeIndex = -1;
        HasPositioned = false;
        ContentOffset.Y = 0;

        EmptyHint.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var line in lines)
        {
            var row = new LyricRow(
                line,
                _fontSize,
                _lineHeight,
                _mainHeight,
                _rowSpacing,
                _translationFontSize,
                _translationLineHeight,
                _translationGap);
            _rows.Add(row);
            LinesHost.Children.Add(row.Root);
        }

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
        UpdateRowOpacity();

        AnimateOffset(OffsetTarget(index));
    }

    /// <summary>
    /// 按到当前行的距离给每行定不透明度：近处为当前行与普通行两档，远端平滑衰减到零。
    /// 淡出范围随可见行数变化，窗口大小改变后边缘仍恰好落在视口上下沿。
    /// </summary>
    private void UpdateRowOpacity()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var distance = Math.Abs(i - _activeIndex);
            var fade = distance <= _fadeStartRows
                ? 1.0
                : CoverFade.SmoothFadeAlpha((distance - _fadeStartRows) / (_fadeEndRows - _fadeStartRows));
            _rows[i].Root.Opacity = (i == _activeIndex ? 1.0 : InactiveAlpha) * fade;
        }
    }

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
            // 首帧直接落位：从第一行开始播放时不该看见整列从顶部滚下来
            ContentOffset.Y = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = ContentOffset.Y,
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(420)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, ContentOffset);
        Storyboard.SetTargetProperty(animation, "Y");
        RunOnce(animation);
    }

    private static void AnimateScale(LyricRow row, double scale)
    {
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = scale,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, row.Scale);
            Storyboard.SetTargetProperty(animation, property);
            RunOnce(animation);
        }
    }

    private static void RunOnce(Timeline animation)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    /// <summary>
    /// 面板尺寸变化：按可用宽高重算字号、行距与可见行数，并把新的行高写回每一行。
    /// 尺寸连续变化期间直接落位，不播位移动画，避免窗口拖动时歌词迟滞。
    /// </summary>
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        Viewport.Height = e.NewSize.Height;
        Viewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };

        var scale = Math.Clamp(
            Math.Min(e.NewSize.Width / BaseViewportWidth, e.NewSize.Height / BaseViewportHeight),
            MinScale,
            MaxScale);
        var metricsChanged = Math.Abs(scale - _scale) >= 0.005;
        var heightChanged = Math.Abs(e.NewSize.Height - _viewportHeight) >= 0.5;
        _viewportHeight = e.NewSize.Height;

        if (metricsChanged)
        {
            _scale = scale;
            _fontSize = Math.Round(BaseFontSize * scale);
            _lineHeight = Math.Round(BaseLineHeight * scale);
            _mainHeight = _lineHeight + Math.Round(4 * scale);
            _rowSpacing = Math.Round(BaseRowSpacing * scale);
            _translationFontSize = Math.Round(_fontSize * 0.68);
            _translationLineHeight = Math.Round(_lineHeight * 0.68);
            _translationGap = Math.Max(1, Math.Round(2 * scale));

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

        if (!metricsChanged && !heightChanged)
        {
            return;
        }

        UpdateRowMetrics();
        UpdateRowOpacity();

        if (HasPositioned && _activeIndex >= 0 && _activeIndex < _rows.Count)
        {
            // 尺寸变化时直接跟手落位，滚动位置始终以新行高为准
            ContentOffset.Y = OffsetTarget(_activeIndex);
        }
    }

    /// <summary>累计每行顶端与总高，并重算可见行数对应的淡出边界。</summary>
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

        var pitch = Math.Max(1, _mainHeight + _rowSpacing);
        var visibleRows = Math.Max(3, (int)Math.Floor(_viewportHeight / pitch));
        _fadeEndRows = Math.Max(2.5, (visibleRows / 2.0) + 0.5);
        _fadeStartRows = Math.Max(1.2, _fadeEndRows - Math.Max(1.2, visibleRows * 0.18));
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

        private readonly double _translationGap;

        public LyricRow(
            LyricLine line,
            double fontSize,
            double lineHeight,
            double mainHeight,
            double rowSpacing,
            double translationFontSize,
            double translationLineHeight,
            double translationGap)
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
                Translation = CreateText(translation, TranslationColor, translationFontSize, translationLineHeight);
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

            _translationGap = translationGap;
            ApplyMetrics(
                fontSize,
                lineHeight,
                mainHeight,
                rowSpacing,
                translationFontSize,
                translationLineHeight,
                translationGap);
        }

        public Grid Root { get; }

        public TextBlock Pending { get; }

        public TextBlock Fill { get; }

        public TextBlock? Translation { get; }

        public ScaleTransform Scale { get; }

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
