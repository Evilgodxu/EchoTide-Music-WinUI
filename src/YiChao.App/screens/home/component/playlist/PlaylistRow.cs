using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using YiChao.Data.Model;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板的一行：把曲目摊平成模板能直接绑的几项，省掉模板里的转换器。
/// 当前播放的那行换掉序号、加一层极淡的底，行内不再单列「正在播放」文字。
/// </summary>
public sealed class PlaylistRow
{
    private static readonly SolidColorBrush CurrentBackground =
        new(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private static readonly SolidColorBrush PlainBackground =
        new(Microsoft.UI.Colors.Transparent);

    public PlaylistRow(MusicTrack track, int index, bool isCurrent)
    {
        Index = index;
        IsCurrent = isCurrent;
        IndexText = (index + 1).ToString();
        Title = track.Title;
        Artist = track.Artist;
        DurationText = FormatDuration(track.Duration);
    }

    public int Index { get; }

    public bool IsCurrent { get; }

    public string IndexText { get; }

    public string Title { get; }

    public string Artist { get; }

    public string DurationText { get; }

    public Visibility IndexVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Brush RowBackground => IsCurrent ? CurrentBackground : PlainBackground;

    /// <summary>分钟数不折成小时：长音频按总分钟读完更直观，也与忆潮的列表一致。</summary>
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
