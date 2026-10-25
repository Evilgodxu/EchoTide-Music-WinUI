namespace YiChao.Data.Model;

/// <summary>
/// 曲目：以文件全路径为标识。元数据在扫描期一次性读入，播放期不再回读文件属性。
/// </summary>
public sealed class MusicTrack
{
    public required string FilePath { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required string Album { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>同名 .lrc 的路径，扫描期建立索引时确认存在；没有歌词为空。</summary>
    public string? LyricPath { get; init; }

    public string FileName => System.IO.Path.GetFileName(FilePath);
}
