using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using YiChao.Data.Metadata;
using YiChao.Data.Model;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板的一行：曲目信息与行内封面。
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

    public string Artist => Track.Artist;

    public string DurationText { get; }

    public ImageSource? Cover => _cover;

    public bool IsCurrent => _isCurrent;

    /// <summary>当前曲目在封面上盖一层暗底并叠播放标记；暂停时只剩底色，与忆潮的行内做法一致。</summary>
    public Visibility CurrentOverlayVisibility =>
        _isCurrent && _isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public Brush RowBackground => _isCurrent ? CurrentBackground : PlainBackground;

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

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>分钟数不折成小时：长音频按总分钟读完更直观，也与忆潮的列表一致。</summary>
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
