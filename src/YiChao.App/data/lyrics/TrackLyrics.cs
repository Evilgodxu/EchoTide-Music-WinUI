using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using YiChao.Data.Model;

namespace YiChao.Data.Lyrics;

/// <summary>
/// 曲目歌词：同名 .lrc 优先，其次本地歌词缓存，最后才解一次容器内嵌歌词并落盘成 LRC。
///
/// 与忆潮同一流程：内嵌歌词不每次实时解码——第一次读到就写成 lyrics 目录下的一份 .lrc，
/// 之后按音频文件的修改时间判定是否过期，过期才重新解。落盘的那份是纯文本，
/// 读盘成本远低于解析容器标签，歌词搜索因此可以按需扫过整库而不必反复解码音频。
///
/// 原文另有一层内存缓存，供播放视图与歌词搜索共用，放满即整体清空。
/// </summary>
public static class TrackLyrics
{
    private const int CacheCapacity = 256;

    private static readonly object Gate = new();

    private static readonly Dictionary<string, string?> TextCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>歌词缓存目录：本地应用数据下，进程重启后仍可用。</summary>
    private static readonly string LyricDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YiChao",
        "lyrics");

    /// <summary>读歌词原文：同名 .lrc → 歌词缓存 → 解内嵌并落盘 → 空。</summary>
    public static string? ReadText(MusicTrack track)
    {
        lock (Gate)
        {
            if (TextCache.TryGetValue(track.FilePath, out var cached))
            {
                return cached;
            }
        }

        var text = ReadSidecar(track)
            ?? ReadCache(track)
            ?? ExtractAndCache(track);

        lock (Gate)
        {
            if (TextCache.Count >= CacheCapacity)
            {
                TextCache.Clear();
            }

            TextCache[track.FilePath] = text;
        }

        return text;
    }

    /// <summary>
    /// 解析成时间轴歌词。没有时间戳的整篇歌词按行铺开、时间统一记 0：
    /// 面板会把这些行全部显示出来，逐行高亮退化为停在首行，比整块留空更接近用户预期。
    /// </summary>
    public static IReadOnlyList<LyricLine> Load(MusicTrack track)
    {
        var text = ReadText(track);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<LyricLine>();
        }

        var lines = LrcParser.Parse(text);
        if (lines.Count > 0)
        {
            return lines;
        }

        return text
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0)
            .Select(line => new LyricLine(0, line))
            .ToList();
    }

    private static string? ReadSidecar(MusicTrack track) =>
        track.LyricPath is { } path ? LrcParser.ReadTextFile(path) : null;

    /// <summary>读缓存文件：缓存早于音频文件即视为过期，返回 null 让它重解。</summary>
    private static string? ReadCache(MusicTrack track)
    {
        try
        {
            var path = CachePath(track);
            if (!File.Exists(path))
            {
                return null;
            }

            if (File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(track.FilePath))
            {
                return null;
            }

            return LrcParser.ReadTextFile(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>解一次内嵌歌词并落盘；解不到就记一个空结果，下次同样不会重复解码。</summary>
    private static string? ExtractAndCache(MusicTrack track)
    {
        var text = EmbeddedLyrics.ReadText(track.FilePath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(LyricDirectory);
            File.WriteAllText(CachePath(track), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            // 落盘失败不影响本次显示：下次再解一次即可
        }

        return text;
    }

    /// <summary>缓存文件名取全路径的散列：路径一次性定音，改名或移动后会重新生成。</summary>
    private static string CachePath(MusicTrack track)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(track.FilePath.ToLowerInvariant()));
        var name = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return Path.Combine(LyricDirectory, name + ".lrc");
    }
}
