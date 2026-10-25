using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using YiChao.Data.Lyrics;
using YiChao.Data.Model;
using YiChao.Data.Playback;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板：铺满窗口自下方升起，列出当前播放队列。
///
/// 与忆潮横屏一致：点曲目即切歌并收起面板；点空白处、按 Esc 或右键收起，不设返回按钮。
/// 面板本身不改队列——切歌、插队、收藏、重扫都上报给上层，由视图模型决定。
///
/// 搜索是提交式的：键入只更新输入框，回车才真正搜（标题/艺术家/专辑先命中，
/// 都没命中的再读歌词找，读到的歌词由 <see cref="TrackLyrics"/> 缓存成 LRC，不会每次重解）。
/// </summary>
public sealed partial class PlaylistPanel : UserControl
{
    private const double SlideMs = 260;

    private const double HiddenOffset = 48;

    /// <summary>行高：36 的封面加上下各 7 的内边距，用于把滚动偏移换算成可视区的项区间。</summary>
    private const double RowHeight = PlaylistRow.ThumbnailEdge + 14;

    /// <summary>歌词搜索的并发读取数：解容器标签是重活，四路足够打满磁盘顺序读。</summary>
    private const int LyricSearchConcurrency = 4;

    private readonly List<PlaylistRow> _rows = new();

    private readonly PlaylistCoverPrefetcher _prefetcher = new();

    private List<PlaylistRow> _viewRows = new();

    private List<SearchResult>? _searchResults;

    private ScrollViewer? _scroll;

    private double _lastOffset;

    private bool _isPlaying;

    private int _currentIndex = -1;

    private PlaylistSortField _sortField = PlaylistSortField.Default;

    private bool _descending;

    private CancellationTokenSource? _searchCts;

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

    /// <summary>选中队列中的某一首，参数为队列下标。</summary>
    public event EventHandler<int>? TrackChosen;

    /// <summary>点了刷新：重扫曲库由上层做，做完回调 <see cref="SetRefreshing"/>。</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>点了行尾的插队键。</summary>
    public event EventHandler<MusicTrack>? PlayNextToggled;

    /// <summary>点了行尾的收藏键。</summary>
    public event EventHandler<MusicTrack>? FavoriteToggled;

    /// <summary>收藏态查询：铺行时据此点亮已收藏的曲目。</summary>
    public Func<MusicTrack, bool>? FavoriteQuery { get; set; }

    /// <summary>插队态查询：铺行时据此显示已插队的曲目。</summary>
    public Func<MusicTrack, bool>? QueuedQuery { get; set; }

    public bool IsOpen => RootLayer.Visibility == Visibility.Visible;

    public void ApplyLanguage()
    {
        TitleText.Text = Strings.Playlist;
        EmptyText.Text = Strings.PlaylistEmpty;
        SearchBox.PlaceholderText = Strings.PlaylistSearchPlaceholder;
        ToolTipService.SetToolTip(RefreshButton, Strings.RefreshLibrary);
        ToolTipService.SetToolTip(SortButton, Strings.SortFieldTitle);
        ToolTipService.SetToolTip(TopButton, Strings.ScrollToTop);
        ToolTipService.SetToolTip(LocateButton, Strings.LocatePlaying);
        ApplyDirectionTooltip();

        SortDefaultItem.Text = Strings.SortDefault;
        SortFavoriteItem.Text = Strings.SortFavorite;
        SortModifiedItem.Text = Strings.SortModified;
        SortTitleItem.Text = Strings.SortTitle;
        SortArtistItem.Text = Strings.SortArtist;
        SortAlbumItem.Text = Strings.SortAlbum;
        SortDurationItem.Text = Strings.SortDuration;
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
        _currentIndex = currentIndex;
        foreach (var row in _rows)
        {
            row.SetState(row.Index == currentIndex, isPlaying);
        }
    }

    /// <summary>刷新是否在进行：期间把刷新键置灰，避免连点。</summary>
    public void SetRefreshing(bool refreshing)
    {
        RefreshButton.IsEnabled = !refreshing;
        RefreshButton.Opacity = refreshing ? 0.4 : 1;
    }

    public void SetFavorite(MusicTrack track, bool favorite) =>
        FindRow(track)?.SetFavorite(favorite);

    public void SetQueued(MusicTrack track, bool queued) =>
        FindRow(track)?.SetQueued(queued);

    /// <summary>Esc 的第一层退让：有搜索词先清空，返回是否消费了这次按键。</summary>
    public bool ClearSearchIfAny()
    {
        if (!IsOpen || string.IsNullOrEmpty(SearchBox.Text))
        {
            return false;
        }

        SearchBox.Text = string.Empty;
        return true;
    }

    public void Hide()
    {
        if (!IsOpen)
        {
            return;
        }

        _prefetcher.Stop();
        _searchCts?.Cancel();
        Fade(false);
    }

    private void Rebuild(IReadOnlyList<MusicTrack> tracks, int currentIndex)
    {
        _rows.Clear();
        for (var i = 0; i < tracks.Count; i++)
        {
            var row = new PlaylistRow(tracks[i], i);
            row.SetState(i == currentIndex, _isPlaying);
            row.SetFavorite(FavoriteQuery?.Invoke(tracks[i]) == true);
            row.SetQueued(QueuedQuery?.Invoke(tracks[i]) == true);
            _rows.Add(row);
        }

        _currentIndex = currentIndex;
        _searchResults = null;
        ApplySort();
        RefreshView(scrollToCurrent: true);
    }

    /// <summary>按当前字段重排展示顺序；队列下标不动，只改行的排列。</summary>
    private void ApplySort()
    {
        var rowsByPath = new Dictionary<string, PlaylistRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            rowsByPath[row.Track.FilePath] = row;
        }

        var ordered = PlaylistSorter.Order(
            _rows.Select(row => row.Track).ToList(),
            _sortField,
            _descending,
            track => rowsByPath.TryGetValue(track.FilePath, out var row) && row.IsFavorite);

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            rank[ordered[i].FilePath] = i;
        }

        _rows.Sort((left, right) => rank[left.Track.FilePath].CompareTo(rank[right.Track.FilePath]));
        UpdateSortChecks();
    }

    /// <summary>
    /// 把展示行绑到列表：有搜索结果就按结果排（标题命中在前，歌词命中按命中行数），否则整份队列。
    /// 行内副标题在歌词命中时换成命中的那句。
    /// </summary>
    private void RefreshView(bool scrollToCurrent = false)
    {
        foreach (var row in _rows)
        {
            row.SetSnippet(null);
        }

        if (_searchResults is null)
        {
            _viewRows = _rows;
        }
        else
        {
            _viewRows = new List<PlaylistRow>(_searchResults.Count);
            foreach (var hit in _searchResults)
            {
                hit.Row.SetSnippet(hit.Snippet);
                _viewRows.Add(hit.Row);
            }
        }

        TrackList.ItemsSource = null;
        TrackList.ItemsSource = _viewRows;

        var empty = _viewRows.Count == 0;
        TrackList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _rows.Count == 0 ? Strings.PlaylistEmpty : Strings.PlaylistNoMatch;
        CountText.Text = Strings.PlaylistCount(_viewRows.Count);
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var tracks = new List<MusicTrack>(_viewRows.Count);
        foreach (var row in _viewRows)
        {
            tracks.Add(row.Track);
        }

        _prefetcher.Start(tracks, PlaylistRow.ThumbnailRequestSize);
        UpdateWindowFromScroll();

        // 只在当前曲目确实在展示列表里才定位：不在其中时列表停在原处，不跳到不相关的行
        if (scrollToCurrent && _viewRows.Any(row => row.Index == _currentIndex))
        {
            ScrollToCurrent();
        }
    }

    private PlaylistRow? FindRow(MusicTrack track) =>
        _rows.FirstOrDefault(row => ReferenceEquals(row.Track, track))
        ?? _rows.FirstOrDefault(row => string.Equals(row.Track.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));

    // ---- 搜索

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // 键入不搜：搜索是提交式的，只有回车（或清空）才真正执行
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (SearchBox.Text.Length == 0 && _searchResults is not null)
        {
            _searchResults = null;
            RefreshView();
        }
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await SubmitSearchAsync();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Text = string.Empty;

    private async Task SubmitSearchAsync()
    {
        var keyword = SearchBox.Text.Trim();

        _searchCts?.Cancel();
        if (keyword.Length == 0)
        {
            _searchResults = null;
            RefreshView();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        var direct = new List<SearchResult>();
        var lyricCandidates = new List<PlaylistRow>();
        foreach (var row in _rows)
        {
            if (row.Matches(keyword))
            {
                direct.Add(new SearchResult(row, null, 0));
            }
            else
            {
                lyricCandidates.Add(row);
            }
        }

        var lyricHits = await SearchLyricsAsync(lyricCandidates, keyword, cts.Token);
        if (cts.IsCancellationRequested)
        {
            return;
        }

        _searchResults = direct
            .Concat(lyricHits.OrderByDescending(hit => hit.Matches))
            .ToList();
        RefreshView();
    }

    /// <summary>
    /// 歌词命中：只在标题/艺术家/专辑都没命中时才查，按命中行数排序，片段取首个命中行。
    /// 歌词原文由 <see cref="TrackLyrics"/> 带缓存提供（内嵌的第一次解完会落盘成 LRC）。
    /// </summary>
    private static async Task<List<SearchResult>> SearchLyricsAsync(
        IReadOnlyList<PlaylistRow> rows,
        string keyword,
        CancellationToken token)
    {
        var hits = new List<SearchResult>();
        var gate = new SemaphoreSlim(LyricSearchConcurrency);

        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync(token);
            try
            {
                var text = await Task.Run(() => TrackLyrics.ReadText(row.Track), token);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var matches = 0;
                string? snippet = null;
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimEnd('\r').Trim();
                    if (trimmed.Length == 0 || !trimmed.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matches++;
                    snippet ??= trimmed;
                }

                if (matches > 0)
                {
                    lock (hits)
                    {
                        hits.Add(new SearchResult(row, snippet, matches));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 新的搜索已接手：本次结果作废
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return hits;
    }

    // ---- 工具栏

    private void OnSortFieldClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<PlaylistSortField>(tag, out var field))
        {
            return;
        }

        _sortField = field;
        ApplySort();
        RefreshView();
    }

    private void OnDirectionClick(object sender, RoutedEventArgs e)
    {
        _descending = !_descending;
        ApplyDirectionIcon();
        ApplyDirectionTooltip();
        ApplySort();
        RefreshView();
    }

    private void ApplyDirectionIcon() => DirectionIcon.IconData = _descending
        ? "M7.41 8.59 L12 13.17 L16.59 8.59 L18 10 L12 16 L6 10 Z"
        : "M7.41 15.41 L12 10.83 L16.59 15.41 L18 14 L12 8 L6 14 Z";

    private void ApplyDirectionTooltip() => ToolTipService.SetToolTip(
        DirectionButton,
        _descending ? Strings.SortDescending : Strings.SortAscending);

    private void UpdateSortChecks()
    {
        SortDefaultItem.IsChecked = _sortField == PlaylistSortField.Default;
        SortFavoriteItem.IsChecked = _sortField == PlaylistSortField.Favorite;
        SortModifiedItem.IsChecked = _sortField == PlaylistSortField.ModifiedTime;
        SortTitleItem.IsChecked = _sortField == PlaylistSortField.Title;
        SortArtistItem.IsChecked = _sortField == PlaylistSortField.Artist;
        SortAlbumItem.IsChecked = _sortField == PlaylistSortField.Album;
        SortDurationItem.IsChecked = _sortField == PlaylistSortField.Duration;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        SetRefreshing(true);
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnScrollToTopClick(object sender, RoutedEventArgs e) => _scroll?.ChangeView(null, 0, null, disableAnimation: false);

    private void OnLocatePlayingClick(object sender, RoutedEventArgs e) => ScrollToCurrent();

    private void ScrollToCurrent()
    {
        var row = _rows.FirstOrDefault(item => item.Index == _currentIndex);
        if (row is not null)
        {
            TrackList.ScrollIntoView(row);
        }
    }

    // ---- 行内动作

    private void OnPlayNextClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaylistRow row })
        {
            PlayNextToggled?.Invoke(this, row.Track);
        }
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaylistRow row })
        {
            FavoriteToggled?.Invoke(this, row.Track);
        }
    }

    // ---- 滚动与进出

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
        if (_scroll is null || _viewRows.Count == 0)
        {
            return;
        }

        var first = Math.Max(0, (int)(_scroll.VerticalOffset / RowHeight));
        var visible = (int)Math.Ceiling(_scroll.ViewportHeight / RowHeight) + 1;
        var last = Math.Min(_viewRows.Count - 1, first + visible);
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

    /// <summary>右键返回：面板内任意位置都算；搜索框上不算，那里右键是输入框自己的编辑菜单。</summary>
    private void OnAnyRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithin(source, SearchBarHost))
        {
            return;
        }

        Hide();
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>搜索结果：标题/艺术家/专辑命中时片段为空，歌词命中时带片段与命中行数。</summary>
    private readonly record struct SearchResult(PlaylistRow Row, string? Snippet, int Matches);
}
