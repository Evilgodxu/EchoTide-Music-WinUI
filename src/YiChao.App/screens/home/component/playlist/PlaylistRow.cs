using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using YiChao.Data.Metadata;
using YiChao.Data.Model;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板的一行：曲目信息、行内封面，以及两枚行尾动作（下一首播放、收藏）。
///
/// 行内封面按行内实际显示尺寸请求（见 <see cref="ThumbnailRequestSize"/>），
/// 构造时先查一次内存缓存——命中就首帧直接出图，不必先闪占位；
/// 未命中的交给视图在行容器就位时补取，缓存里已由邻域预取提前放好。
/// </summary>
public sealed class PlaylistRow : INotifyPropertyChanged
{
    /// <summary>行内封面显示边长，与模板里的缩略图尺寸一致。</summary>
    public const int ThumbnailEdge = 36;

    /// <summary>请求边长：按 32 的整数倍上取，与预取共用同一个值，否则预取结果无法被行命中。</summary>
    public static readonly int ThumbnailRequestSize = CoverThumbnailCache.RequestSize(ThumbnailEdge);

    private static readonly SolidColorBrush CurrentBackground =
        new(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private static readonly SolidColorBrush PlainBackground =
        new(Microsoft.UI.Colors.Transparent);

    private ImageSource? _cover;

    private bool _isCurrent;

    private bool _isPlaying;

    private bool _isFavorite;

    private bool _isQueued;

    private string? _snippet;

    private string? _highlight;

    public PlaylistRow(MusicTrack track, int index)
    {
        Track = track;
        Index = index;
        DurationText = FormatDuration(track.Duration);
        _cover = CoverThumbnailCache.Peek(track.FilePath, ThumbnailRequestSize);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MusicTrack Track { get; }

    public int Index { get; }

    public string Title => Track.Title;

    /// <summary>副标题：默认艺术家；歌词命中时换成命中的那句歌词。</summary>
    public string Subtitle => _snippet ?? Track.Artist;

    /// <summary>副标题中需要强调的关键词；仅歌词命中时非空，艺术家副标题不强调。</summary>
    public string? Highlight => _highlight;

    public string DurationText { get; }

    public ImageSource? Cover => _cover;

    public bool IsCurrent => _isCurrent;

    public bool IsFavorite => _isFavorite;

    /// <summary>当前曲目在封面上盖一层暗底并叠播放标记；暂停时只剩底色，与忆潮的行内做法一致。</summary>
    public Visibility CurrentOverlayVisibility =>
        _isCurrent && _isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public Brush RowBackground => _isCurrent ? CurrentBackground : PlainBackground;

    public Visibility FavoriteOnVisibility => _isFavorite ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FavoriteOffVisibility => _isFavorite ? Visibility.Collapsed : Visibility.Visible;

    public Visibility QueuedVisibility => _isQueued ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NotQueuedVisibility => _isQueued ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>标题、艺术家、专辑任一命中即算匹配，忽略大小写。</summary>
    public bool Matches(string query) =>
        Track.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Track.Album.Contains(query, StringComparison.OrdinalIgnoreCase);

    public async Task EnsureCoverAsync()
    {
        if (_cover is not null)
        {
            return;
        }

        var cover = await CoverThumbnailCache.LoadAsync(Track.FilePath, ThumbnailRequestSize);
        if (cover is null || _cover is not null)
        {
            return;
        }

        _cover = cover;
        Raise(nameof(Cover));
    }

    public void SetState(bool current, bool playing)
    {
        var overlayChanged = (_isCurrent && _isPlaying) != (current && playing);
        var backgroundChanged = _isCurrent != current;
        _isCurrent = current;
        _isPlaying = playing;

        if (backgroundChanged)
        {
            Raise(nameof(IsCurrent));
            Raise(nameof(RowBackground));
        }

        if (overlayChanged)
        {
            Raise(nameof(CurrentOverlayVisibility));
        }
    }

    public void SetFavorite(bool favorite)
    {
        if (_isFavorite == favorite)
        {
            return;
        }

        _isFavorite = favorite;
        Raise(nameof(FavoriteOnVisibility));
        Raise(nameof(FavoriteOffVisibility));
    }

    public void SetQueued(bool queued)
    {
        if (_isQueued == queued)
        {
            return;
        }

        _isQueued = queued;
        Raise(nameof(QueuedVisibility));
        Raise(nameof(NotQueuedVisibility));
    }

    /// <summary>歌词命中片段与其中要强调的关键词；传 null 恢复显示艺术家且不强调。</summary>
    public void SetSnippet(string? snippet, string? highlight = null)
    {
        if (string.Equals(_snippet, snippet, StringComparison.Ordinal)
            && string.Equals(_highlight, highlight, StringComparison.Ordinal))
        {
            return;
        }

        _snippet = snippet;
        _highlight = highlight;
        Raise(nameof(Subtitle));
        Raise(nameof(Highlight));
    }

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>分钟数不折成小时：长音频按总分钟读完更直观，也与忆潮的列表一致。</summary>
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
