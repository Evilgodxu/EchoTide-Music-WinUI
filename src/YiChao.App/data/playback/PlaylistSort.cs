using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using YiChao.Data.Model;

namespace YiChao.Data.Playback;

/// <summary>播放列表排序字段，与忆潮的 PlaylistSortField 同名同序。</summary>
public enum PlaylistSortField
{
    Default,
    Favorite,
    ModifiedTime,
    Title,
    Artist,
    Album,
    Duration,
}

/// <summary>
/// 播放列表排序：只改展示顺序，不动播放队列本身。
///
/// 文本比较走系统对 zh-CN 的排序（中文按拼音、英文按字母），首字符为数字的排在前并按数值升序；
/// 收藏字段已收藏者靠前，同一收藏态内仍按标题自然序；新旧按文件修改时间、新在前。
/// </summary>
public static class PlaylistSorter
{
    private static readonly CultureInfo SortCulture = CultureInfo.GetCultureInfo("zh-CN");

    public static IReadOnlyList<MusicTrack> Order(
        IReadOnlyList<MusicTrack> tracks,
        PlaylistSortField field,
        bool descending,
        Func<MusicTrack, bool> isFavorite)
    {
        var ordered = field switch
        {
            PlaylistSortField.Favorite => tracks
                .OrderByDescending(isFavorite)
                .ThenBy(track => track.Title, Comparer<string>.Create(CompareText))
                .ToList(),

            PlaylistSortField.ModifiedTime => tracks
                .OrderByDescending(ModifiedTime)
                .ToList(),

            PlaylistSortField.Title => tracks
                .OrderBy(track => track.Title, Comparer<string>.Create(CompareText))
                .ToList(),

            PlaylistSortField.Artist => tracks
                .OrderBy(track => track.Artist, Comparer<string>.Create(CompareText))
                .ThenBy(track => track.Title, Comparer<string>.Create(CompareText))
                .ToList(),

            PlaylistSortField.Album => tracks
                .OrderBy(track => track.Album, Comparer<string>.Create(CompareText))
                .ThenBy(track => track.Title, Comparer<string>.Create(CompareText))
                .ToList(),

            PlaylistSortField.Duration => tracks
                .OrderBy(track => track.Duration)
                .ThenBy(track => track.Title, Comparer<string>.Create(CompareText))
                .ToList(),

            // 默认：沿用曲库顺序（扫描期已按标题排过），这里不动
            _ => tracks.ToList(),
        };

        if (descending)
        {
            ordered.Reverse();
        }

        return ordered;
    }

    /// <summary>文件修改时间；读不到按最早处理，不让一次 IO 失败打乱整次排序。</summary>
    private static DateTime ModifiedTime(MusicTrack track)
    {
        try
        {
            return File.GetLastWriteTime(track.FilePath);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>首字母自然序：数字开头者排前且按数值比较，其余交给文化排序。</summary>
    private static int CompareText(string? left, string? right)
    {
        var leftText = left ?? string.Empty;
        var rightText = right ?? string.Empty;

        var leftNumber = LeadingNumber(leftText);
        var rightNumber = LeadingNumber(rightText);
        if (leftNumber.HasValue && rightNumber.HasValue)
        {
            var byNumber = leftNumber.Value.CompareTo(rightNumber.Value);
            if (byNumber != 0)
            {
                return byNumber;
            }
        }
        else if (leftNumber.HasValue)
        {
            return -1;
        }
        else if (rightNumber.HasValue)
        {
            return 1;
        }

        return string.Compare(leftText, rightText, SortCulture, CompareOptions.IgnoreCase);
    }

    private const string ChineseDigits = "零一二三四五六七八九";

    /// <summary>取开头数字的数值：阿拉伯数字取连续数字串，中文数字取单字；非数字开头返回 null。</summary>
    private static long? LeadingNumber(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var first = trimmed[0];
        if (first is >= '0' and <= '9')
        {
            var end = 0;
            while (end < trimmed.Length && trimmed[end] is >= '0' and <= '9')
            {
                end++;
            }

            return long.TryParse(trimmed[..end], out var value) ? value : null;
        }

        var index = ChineseDigits.IndexOf(first);
        return index >= 0 ? index : null;
    }
}
