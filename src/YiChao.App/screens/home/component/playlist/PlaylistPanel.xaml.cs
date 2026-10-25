using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using YiChao.Data.Model;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板：铺满窗口自下方升起，列出当前播放队列。
///
/// 与忆潮横屏一致：点曲目即切歌并收起面板；点空白处、按 Esc 或右键收起，不设返回按钮。
/// 面板本身不改队列，选哪一首由上层决定。
/// 行内封面按行尺寸取系统缩略图，可见区前后由预取器提前解码进内存缓存（见 <see cref="PlaylistCoverPrefetcher"/>）。
/// </summary>
public sealed partial class PlaylistPanel : UserControl
{
    private const double SlideMs = 260;

    private const double HiddenOffset = 48;

    /// <summary>行高：36 的封面加上下各 7 的内边距，用于把滚动偏移换算成可视区的项区间。</summary>
    private const double RowHeight = PlaylistRow.ThumbnailEdge + 14;

    private readonly List<PlaylistRow> _rows = new();

    private readonly PlaylistCoverPrefetcher _prefetcher = new();

    private ScrollViewer? _scroll;

    private double _lastOffset;

    private bool _isPlaying;

    public PlaylistPanel()
    {
        InitializeComponent();
        ApplyLanguage();

        TrackList.ContainerContentChanging += (list, args) =>
        {
            if (!args.InRecycleQueue && args.Item is PlaylistRow row)
            {
                _ = row.EnsureCoverAsync();
            }
        };

        TrackList.Loaded += (_, _) => HookScrollViewer();
        TrackList.SizeChanged += (_, _) => UpdateWindowFromScroll();

        // 列表项会把右键事件当作自己的手势处理掉，冒泡到这里时已被标记 handled：
        // 这里挂 handledEventsToo，面板内任意位置的右键都能收起
        AddHandler(RightTappedEvent, new RightTappedEventHandler(OnAnyRightTapped), handledEventsToo: true);
    }

    /// <summary>选中队列中的某一首，参数为下标。</summary>
    public event EventHandler<int>? TrackChosen;

    public bool IsOpen => RootLayer.Visibility == Visibility.Visible;

    public void ApplyLanguage()
    {
        TitleText.Text = Strings.Playlist;
        EmptyText.Text = Strings.PlaylistEmpty;
    }

    /// <summary>按当前队列重铺一遍再升起；<paramref name="currentIndex"/> 用于定位并标记当前曲目。</summary>
    public void Show(IReadOnlyList<MusicTrack> tracks, int currentIndex, bool isPlaying)
    {
        _isPlaying = isPlaying;
        Rebuild(tracks, currentIndex);

        if (!IsOpen)
        {
            RootLayer.Visibility = Visibility.Visible;
            Fade(true);
        }

        // 升起后再量一次可视区：收起期间布局尺寸为 0，窗口区间要等真正显示才算得准
        UpdateWindowFromScroll();
    }

    /// <summary>播放状态或当前曲目变了：只挪标记，不重铺列表，滚动位置留在原处。</summary>
    public void UpdateState(int currentIndex, bool isPlaying)
    {
        _isPlaying = isPlaying;
        foreach (var row in _rows)
        {
            row.SetState(row.Index == currentIndex, isPlaying);
        }
    }

    public void Hide()
    {
        if (IsOpen)
        {
            _prefetcher.Stop();
            Fade(false);
        }
    }

    private void Rebuild(IReadOnlyList<MusicTrack> tracks, int currentIndex)
    {
        _rows.Clear();
        for (var i = 0; i < tracks.Count; i++)
        {
            var row = new PlaylistRow(tracks[i], i);
            row.SetState(i == currentIndex, _isPlaying);
            _rows.Add(row);
        }

        TrackList.ItemsSource = null;
        TrackList.ItemsSource = _rows;
        CountText.Text = Strings.PlaylistCount(_rows.Count);

        var empty = _rows.Count == 0;
        TrackList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty && currentIndex >= 0 && currentIndex < _rows.Count)
        {
            TrackList.ScrollIntoView(_rows[currentIndex]);
        }

        _prefetcher.Start(tracks, PlaylistRow.ThumbnailRequestSize);
    }

    private void HookScrollViewer()
    {
        _scroll ??= FindScrollViewer(TrackList);
        if (_scroll is null)
        {
            return;
        }

        _scroll.ViewChanged -= OnScrollViewChanged;
        _scroll.ViewChanged += OnScrollViewChanged;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateWindowFromScroll();

    /// <summary>
    /// 把滚动偏移换算成可视区的项区间并发布给预取器。
    /// 行高固定，按偏移与视口高度整除即可，不必逐项问容器位置。
    /// </summary>
    private void UpdateWindowFromScroll()
    {
        if (_scroll is null || _rows.Count == 0)
        {
            return;
        }

        var first = Math.Max(0, (int)(_scroll.VerticalOffset / RowHeight));
        var visible = (int)Math.Ceiling(_scroll.ViewportHeight / RowHeight) + 1;
        var last = Math.Min(_rows.Count - 1, first + visible);
        var forward = _scroll.VerticalOffset >= _lastOffset;
        _lastOffset = _scroll.VerticalOffset;

        _prefetcher.UpdateWindow(first, last, forward);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>进出：整块面板自下浮起，遮罩同时淡入淡出。</summary>
    private void Fade(bool open)
    {
        if (open)
        {
            HookScrollViewer();
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(SlideMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        void Animate(DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        Animate(Panel, "Opacity", Panel.Opacity, open ? 1 : 0);
        Animate(Backdrop, "Opacity", Backdrop.Opacity, open ? 1 : 0);
        Animate(PanelOffset, "Y", PanelOffset.Y, open ? 0 : HiddenOffset);

        storyboard.Completed += (_, _) =>
        {
            Panel.Opacity = open ? 1 : 0;
            Backdrop.Opacity = open ? 1 : 0;
            PanelOffset.Y = open ? 0 : HiddenOffset;

            if (!open)
            {
                RootLayer.Visibility = Visibility.Collapsed;
            }
        };

        storyboard.Begin();
    }

    private void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistRow row)
        {
            TrackChosen?.Invoke(this, row.Index);
        }
    }

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e) => Hide();

    /// <summary>右键返回：面板内任意位置都算，不必非得点空白处。</summary>
    private void OnAnyRightTapped(object sender, RightTappedRoutedEventArgs e) => Hide();
}
