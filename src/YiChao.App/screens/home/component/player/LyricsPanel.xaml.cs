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
/// 横屏歌词面板：当前行居中、上下各留五行，整列随播放进度平移，当前行按字序点亮并放大。
///
/// 逐字点亮用两层同排版文本叠放：底层整行按未唱色铺满，上层同色更高的文本按进度矩形裁剪，
/// 裁剪宽度随播放位置推进，读起来就是一个从左到右的扫光。WinUI 没有逐字文本布局，
/// 用双层裁剪代替逐字定位——观感一致，代价是没有忆潮那种逐字的弹簧跳动。
///
/// 所有行一次性建好并整体平移，不按窗口虚拟化：切歌时重建一次，播放期只有一行在变（裁剪与两行的高亮切换），
/// 逐帧代价与行数无关。
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    private const double LyricFontSize = 18;
    private const double LyricLineHeight = 24;

    /// <summary>单行占位高度：行高加行内上下留白，与忆潮的 standardSlotHeight 同口径。</summary>
    private const double RowHeight = LyricLineHeight + 4;

    private const double RowSpacing = 10;
    private const int VisibleLines = 11;

    /// <summary>视口固定为 N 行标准高加行距：超长句换行与译文叠层不参与排布，溢出交由裁剪处理。</summary>
    private const double ViewportHeight = (RowHeight * VisibleLines) + (RowSpacing * (VisibleLines - 1));

    /// <summary>上下边缘开始衰减的行距：视口 11 行时约第 4 行起淡出。</summary>
    private const double FadeStartRows = 3.9;

    /// <summary>完全透明的行距：视口边缘。</summary>
    private const double FadeEndRows = 5.5;

    private const double RowPitch = RowHeight + RowSpacing;
    private const double InactiveAlpha = 0.55;
    private const double InactiveScale = 0.98;
    private const double ActiveScale = 1.14;
    private const double PendingAlpha = 0.72;

    /// <summary>末行没有下一行时间戳时按 3 秒点亮完毕，与忆潮一致。</summary>
    private const long LastLineFallbackDurationMs = 3000;

    private static readonly Color PendingColor = Color.FromArgb((byte)Math.Round(PendingAlpha * 255), 0xFF, 0xFF, 0xFF);
    private static readonly Color ActiveColor = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    private readonly List<LyricRow> _rows = new();
    private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
    private int _activeIndex = -1;

    public LyricsPanel()
    {
        InitializeComponent();
        Viewport.Height = ViewportHeight;
        EmptyHint.Text = Strings.NoLyrics;
        EmptyHint.Visibility = Visibility.Collapsed;
        ContentOffset.Y = 0;
    }

    public bool HasPositioned { get; private set; }

    /// <summary>语言切换后重取占位文案。</summary>
    public void ApplyLanguage() => EmptyHint.Text = Strings.NoLyrics;

    public void SetLines(IReadOnlyList<LyricLine> lines)
    {
        _lines = lines;
        _rows.Clear();
        LinesHost.Children.Clear();
        _activeIndex = -1;
        HasPositioned = false;
        ContentOffset.Y = 0;

        EmptyHint.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var line in lines)
        {
            var row = new LyricRow(line.Text);
            _rows.Add(row);
            LinesHost.Children.Add(row.Root);
        }
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
            AnimateScale(previous, InactiveScale);
        }

        _activeIndex = index;
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        var current = _rows[index];
        AnimateScale(current, ActiveScale);
        UpdateRowOpacity();

        var target = (ViewportHeight / 2) - ((index * RowPitch) + (RowHeight / 2));
        AnimateOffset(target);
    }

    /// <summary>
    /// 按到当前行的距离给每行定不透明度：近处为当前行与普通行两档，远端平滑衰减到零。
    /// 忆潮用一层遮罩淡出视口两端，这里改成逐行折算——WinUI 3 没有 OpacityMask，
    /// 而衰减本来就只随行距变化，逐行给出与遮罩是同一个结果。
    /// </summary>
    private void UpdateRowOpacity()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var distance = Math.Abs(i - _activeIndex);
            var fade = distance <= FadeStartRows
                ? 1.0
                : CoverFade.SmoothFadeAlpha((distance - FadeStartRows) / (FadeEndRows - FadeStartRows));
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

        row.FillClip.Rect = new Rect(0, 0, width * fraction, RowHeight);
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

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 视口裁剪按实际尺寸，内容溢出与滚出视口的行在此被切掉
        Viewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    /// <summary>一行歌词：两层同排版文本 + 各自的高亮态变换。</summary>
    private sealed class LyricRow
    {
        public Grid Root { get; }

        public TextBlock Pending { get; }

        public TextBlock Fill { get; }

        public ScaleTransform Scale { get; }

        public RectangleGeometry FillClip { get; }

        public LyricRow(string text)
        {
            Scale = new ScaleTransform { ScaleX = InactiveScale, ScaleY = InactiveScale };
            FillClip = new RectangleGeometry();

            Pending = CreateText(text, PendingColor);
            Fill = CreateText(text, ActiveColor);
            Fill.Clip = null;

            Root = new Grid
            {
                Height = RowHeight,
                Margin = new Thickness(0, 0, 0, RowSpacing),
                Opacity = InactiveAlpha,
                RenderTransform = Scale,
                // 缩放围绕行中心，放大后仍与中线对齐
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Root.Children.Add(Pending);
            Root.Children.Add(Fill);
        }

        private static TextBlock CreateText(string text, Color color) => new()
        {
            Text = text,
            FontSize = LyricFontSize,
            LineHeight = LyricLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
    }
}
