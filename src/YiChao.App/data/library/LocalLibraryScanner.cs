using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using YiChao.Data.Metadata.Tags;
using YiChao.Data.Model;

namespace YiChao.Data.Library;

/// <summary>扫描进度：已读元数据的数量与待读总数。</summary>
public readonly record struct ScanProgress(int Done, int Total);

/// <summary>
/// 本地曲库扫描，口径对齐忆潮的 MediaStore 扫描：一次枚举拿全部候选，元数据一次读完，不逐首走平台接口。
///
/// 平台索引在 Windows 上不可靠：Shell 属性系统每次取属性都要经 Shell 重新打开文件（实测约 2~3ms/首），
/// 而 Windows Search 索引只收录被索引的目录——任意音乐目录往往一行都查不到。
/// 所以改为直接读各容器的标签块（见 <see cref="AudioTagReader"/>），单首在百微秒量级，与目录位置无关。
///
/// 封面不在扫描期读取（同忆潮：逐曲读文件会让耗时随曲库线性增长），由显示端按当前曲目现取。
/// </summary>
public static class LocalLibraryScanner
{
    /// <summary>
    /// 曲库白名单：判据是本机能解码的容器。解码不了的留着只会在曲库里添乱，
    /// 故在扫描期即排除。扩展名取自文件名最后一段，目录名里的点号不参与判定。
    /// </summary>
    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".flac", ".wav", ".m4a", ".m4b", ".mp4", ".aac", ".wma", ".ogg", ".opus",
    };

    private const string LyricExtension = ".lrc";

    /// <summary>最短时长：与忆潮的 DURATION ≥ 30000 同口径，滤掉提示音与片头音效。</summary>
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 平台音乐库：取系统「音乐」库登记的全部目录。
    ///
    /// 这是 Windows 自带的媒体位置登记——用户把 E:\音乐 加进音乐库后，这里就能读到，
    /// 不必逐个目录让用户手选。非打包应用未必拿到库访问权，取不到就返回空，
    /// 由调用方转入手选目录的流程。
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetPlatformMusicFoldersAsync()
    {
        try
        {
            var library = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Music);
            return library.Folders
                .Select(folder => folder.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .ToList();
        }
        catch (Exception)
        {
            // 没有库访问权（非打包或策略限制）时按「平台没有提供」处理
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 扫描若干目录并合并成一份曲库。重复目录、目录内的重复文件按全路径去重，
    /// 多个目录互相包含时同一首歌也只会出现一次。
    /// </summary>
    public static async Task<List<MusicTrack>> ScanAsync(
        IReadOnlyList<string> roots,
        IProgress<ScanProgress>? progress = null)
    {
        var files = EnumerateFiles(roots);
        var lyricIndex = BuildLyricIndex(roots);

        progress?.Report(new ScanProgress(0, files.Count));
        var tracks = await ReadTracksAsync(files, lyricIndex, progress);

        // 曲库按标题排序，同名时以路径定序，保证两次扫描顺序一致
        tracks.Sort((a, b) =>
        {
            var byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            return byTitle != 0 ? byTitle : string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        });
        return tracks;
    }

    /// <summary>枚举全部候选音频文件：纯目录遍历，不触发任何平台接口。</summary>
    private static List<string> EnumerateFiles(IReadOnlyList<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> found;
            try
            {
                found = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                // 单个目录不可读（权限、断开的盘符）只跳过它
                continue;
            }

            try
            {
                foreach (var path in found)
                {
                    if (IsAudioFile(path) && seen.Add(path))
                    {
                        files.Add(path);
                    }
                }
            }
            catch (Exception)
            {
                // 遍历中途遇到不可读的子目录时保留已找到的部分
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static bool IsAudioFile(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var candidate in AudioExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 并行读标签。标签解析是本地字节处理，按核数铺开即可；
    /// 逐首串行等 IO 才会让耗时随曲库线性增长。
    /// </summary>
    private static Task<List<MusicTrack>> ReadTracksAsync(
        IReadOnlyList<string> files,
        Dictionary<string, string> lyricIndex,
        IProgress<ScanProgress>? progress)
    {
        var tracks = new ConcurrentBag<MusicTrack>();
        var done = 0;

        return Task.Run(() =>
        {
            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                path =>
                {
                    var track = ReadTrack(path, lyricIndex);
                    if (track is not null)
                    {
                        tracks.Add(track);
                    }

                    progress?.Report(new ScanProgress(Interlocked.Increment(ref done), files.Count));
                });

            return tracks.ToList();
        });
    }

    private static MusicTrack? ReadTrack(string path, Dictionary<string, string> lyricIndex)
    {
        var tag = AudioTagReader.TryRead(path, out var parsed) ? parsed : default;

        // 太短的按提示音/音效处理，不进曲库；时长读不出来时保留，宁可多留不可误删
        if (tag.Duration > TimeSpan.Zero && tag.Duration < MinimumDuration)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var lyricPath = lyricIndex.GetValueOrDefault(Key(directory, Path.GetFileNameWithoutExtension(path)));
        return new MusicTrack
        {
            FilePath = path,
            Title = tag.Title ?? Path.GetFileNameWithoutExtension(path),
            Artist = tag.Artist ?? "未知艺术家",
            Album = tag.Album ?? string.Empty,
            Duration = tag.Duration,
            LyricPath = lyricPath,
        };
    }

    private static Dictionary<string, string> BuildLyricIndex(IReadOnlyList<string> roots)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*" + LyricExtension, SearchOption.AllDirectories))
                {
                    index[Key(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path))] = path;
                }
            }
            catch (Exception)
            {
                // 不可读的目录只跳过
            }
        }

        return index;
    }

    private static string Key(string directory, string fileNameWithoutExtension) =>
        Path.Combine(directory, fileNameWithoutExtension).ToLowerInvariant();
}
