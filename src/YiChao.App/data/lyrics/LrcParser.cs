using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace YiChao.Data.Lyrics;

/// <summary>逐字歌词中的字：绝对毫秒起点、时长、文本。</summary>
public readonly record struct LyricWord(long StartMs, long DurationMs, string Text);

/// <summary>一行歌词：时间戳（毫秒）、文本、逐字时间轴、译文。</summary>
public readonly record struct LyricLine(
    long TimeMs,
    string Text,
    IReadOnlyList<LyricWord> Words,
    string? Translation)
{
    public LyricLine(long timeMs, string text)
        : this(timeMs, text, Array.Empty<LyricWord>(), null)
    {
    }
}

/// <summary>
/// LRC 解析：与忆潮 Android 版的 parseWordTimedLrcText 同口径。
///
/// 支持普通 LRC（逐行）、增强 LRC（行内逐字 <mm:ss.xxx> 标签）与 [tr][/tr] 译文块。
/// 时间戳小时制 [hh:mm:ss.xxx] 也覆盖，长音频/现场录音不会截断。
/// 同行叠写的多个时间戳（副歌复用）按每个时间戳各成一行：安卓版只取首个时间戳，
/// 会把后续标签留在文本里，Windows 保留展开行为，避免歌词语句被时间标签污染。
/// 解析结果最终按时间正序排序。
///
/// 编码先按 BOM 判定，无 BOM 时回到 UTF-8 严格解码，失败再落到 GB18030——
/// 中文歌词文件这两个来源占绝大多数，猜错的代价是整篇乱码，故用严格解码探测而不是替换字符兜底。
/// </summary>
public static partial class LrcParser
{
    /// <summary>行级时间戳：支持 [mm:ss]、[mm:ss.xxx] 与小时制 [hh:mm:ss.xxx]。</summary>
    [GeneratedRegex(@"\[(?:(\d+):)?(\d+):(\d+)(?:\.(\d+))?\]")]
    private static partial Regex TimeTagPattern();

    /// <summary>逐字时间戳：&lt;mm:ss.xxx&gt; 或小时制 &lt;hh:mm:ss.xxx&gt; 后紧跟字文本。</summary>
    [GeneratedRegex(@"<(?:(\d+):)?(\d+):(\d+)(?:\.(\d+))?>([^<]*)")]
    private static partial Regex WordTagPattern();

    /// <summary>翻译段：行内以 [tr]…[/tr] 包裹。整段抽取为译文。</summary>
    [GeneratedRegex(@"\[tr](.*?)\[/tr]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TranslationPattern();

    public static IReadOnlyList<LyricLine> ParseFile(string path)
    {
        var text = ReadTextFile(path);
        return text is null ? Array.Empty<LyricLine>() : Parse(text);
    }

    /// <summary>读歌词文件原文：编码先按 BOM 判定，无 BOM 时 UTF-8 严格解码，失败回落 GB18030。</summary>
    public static string? ReadTextFile(string path)
    {
        try
        {
            return ReadText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<LyricLine> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<LyricLine>();
        }

        var lines = new List<LyricLine>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var timeMatches = TimeTagPattern().Matches(line);
            if (timeMatches.Count == 0)
            {
                continue;
            }

            // 字标签与译文都写在最后一个时间戳之后
            var lastTimeTag = timeMatches[^1];
            var contentBody = line[(lastTimeTag.Index + lastTimeTag.Length)..];

            // 先提取译文
            string? translation = null;
            var transMatch = TranslationPattern().Match(contentBody);
            if (transMatch.Success)
            {
                translation = transMatch.Groups[1].Value.Trim();
                if (translation.Length == 0)
                {
                    translation = null;
                }
            }

            // 剥离译文块后再切逐字标签
            var cleanContent = TranslationPattern().Replace(contentBody, string.Empty).Trim();

            // 提取所有字标签
            var wordMatches = WordTagPattern().Matches(cleanContent);
            List<LyricWord> words = new();
            foreach (Match tagMatch in wordMatches)
            {
                var wordText = tagMatch.Groups[5].Value;
                if (wordText.Length == 0)
                {
                    continue;
                }

                words.Add(new LyricWord(
                    StartMs: ColonTimestamp(tagMatch.Groups[1].Value, tagMatch.Groups[2].Value, tagMatch.Groups[3].Value, tagMatch.Groups[4].Value),
                    DurationMs: 0,
                    Text: wordText
                ));
            }

            // 拼接整行文本
            var fullText = words.Count > 0
                ? string.Join(string.Empty, words.ConvertAll(w => w.Text))
                : cleanContent;
            fullText = fullText.Trim();

            if (fullText.Length == 0)
            {
                continue;
            }

            var lineWords = words.AsReadOnly();
            foreach (Match timeMatch in timeMatches)
            {
                lines.Add(new LyricLine(
                    TimeMs: ColonTimestamp(timeMatch.Groups[1].Value, timeMatch.Groups[2].Value, timeMatch.Groups[3].Value, timeMatch.Groups[4].Value),
                    Text: fullText,
                    Words: lineWords,
                    Translation: translation
                ));
            }
        }

        // 与安卓版一致用稳定排序：同一时间戳的行保持文件内顺序
        return lines.OrderBy(line => line.TimeMs).ToList();
    }

    /// <summary>形如 hh:mm:ss.xxx / mm:ss.xxx 的时间戳归一为绝对毫秒；小时段可省略。</summary>
    private static long ColonTimestamp(string hoursStr, string minutesStr, string secondsStr, string fractionStr)
    {
        var hours = long.TryParse(hoursStr, out var h) ? h : 0L;
        var minutes = long.Parse(minutesStr);
        var seconds = long.Parse(secondsStr);

        // 小数位统一 padEnd(3, '0') 后 take(3)，保证 1 位 → 100ms、2 位 → 10ms、3 位直接原值
        var fraction = fractionStr.PadRight(3, '0').AsSpan(0, 3);
        var fractionMs = long.Parse(fraction);

        return hours * 3600_000L
            + minutes * 60_000L
            + seconds * 1_000L
            + fractionMs;
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Gb18030.GetString(bytes);
        }
    }

    private static Encoding Gb18030
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030");
        }
    }
}
