using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace YiChao.Data.Lyrics;

/// <summary>一行歌词：时间戳（毫秒）与文本。</summary>
public readonly record struct LyricLine(long TimeMs, string Text);

/// <summary>
/// LRC 解析：时间为正序，一首多行时间戳的行按每个时间戳各成一行。
///
/// 编码先按 BOM 判定，无 BOM 时回到 UTF-8 严格解码，失败再落到 GB18030——
/// 中文歌词文件这两个来源占绝大多数，猜错的代价是整篇乱码，故用严格解码探测而不是替换字符兜底。
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTagRegex();

    public static IReadOnlyList<LyricLine> ParseFile(string path)
    {
        try
        {
            return Parse(ReadText(path));
        }
        catch (IOException)
        {
            return Array.Empty<LyricLine>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<LyricLine>();
        }
    }

    public static IReadOnlyList<LyricLine> Parse(string content)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = TimeTagRegex().Matches(line);
            if (matches.Count == 0)
            {
                // 元信息标签（[ti:]、[ar:]）与空行不产生歌词行
                continue;
            }

            var text = line[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var fractionMs = 0;
                if (match.Groups[3].Success)
                {
                    var fraction = match.Groups[3].Value;
                    // 一位为十分之一秒、两位为百分之一秒、三位为毫秒
                    fractionMs = int.Parse(fraction) * (fraction.Length switch { 1 => 100, 2 => 10, _ => 1 });
                }

                lines.Add(new LyricLine(((minutes * 60L) + seconds) * 1000L + fractionMs, text));
            }
        }

        lines.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return lines;
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
