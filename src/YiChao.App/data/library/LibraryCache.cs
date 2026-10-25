using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using YiChao.Data.Model;

namespace YiChao.Data.Library;

/// <summary>
/// 曲库快照：把上次扫描的结果落盘，启动时先读它，界面不必等重扫。
///
/// 与忆潮把曲库交给 MediaStore 常驻同理——曲目清单不该每次启动重新解析。
/// 快照按目录集合校验：目录一改即失效，宁可重扫也不拿旧清单冒充。
/// 重扫在后台进行，回来再与快照对账，所以快照过期不会让界面停住。
/// </summary>
public static class LibraryCache
{
    /// <summary>快照结构版本：字段增删时递增，旧文件直接作废。</summary>
    private const int SchemaVersion = 1;

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YiChao",
        "library.json");

    public static IReadOnlyList<MusicTrack>? TryLoad(IReadOnlyList<string> folders)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (cache is null || cache.Version != SchemaVersion || !SameFolders(cache.Folders, folders))
            {
                return null;
            }

            return cache.Tracks
                .Select(track => new MusicTrack
                {
                    FilePath = track.Path,
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    Duration = TimeSpan.FromTicks(track.DurationTicks),
                    LyricPath = track.LyricPath,
                })
                .ToList();
        }
        catch (Exception)
        {
            // 快照损坏按没有快照处理，走一次完整扫描即可
            return null;
        }
    }

    /// <summary>写入快照。落盘放在线程池上，不拖慢扫描的收尾。</summary>
    public static void Save(IReadOnlyList<string> folders, IReadOnlyList<MusicTrack> tracks)
    {
        var cache = new CacheFile(
            SchemaVersion,
            folders.ToList(),
            tracks.Select(track => new CachedTrack(
                track.FilePath,
                track.Title,
                track.Artist,
                track.Album,
                track.Duration.Ticks,
                track.LyricPath)).ToList());

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
            }
            catch (Exception)
            {
                // 写不进去只影响下次启动的装载速度，不影响曲库本身
            }
        });
    }

    private static bool SameFolders(List<string> cached, IReadOnlyList<string> current)
    {
        var cachedSet = new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        return cachedSet.SetEquals(currentSet);
    }

    private sealed record CacheFile(int Version, List<string> Folders, List<CachedTrack> Tracks);

    private sealed record CachedTrack(
        string Path,
        string Title,
        string Artist,
        string Album,
        long DurationTicks,
        string? LyricPath);
}
