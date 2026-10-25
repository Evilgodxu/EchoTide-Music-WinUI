using System;
using System.IO;
using System.Text;

namespace YiChao.Data.Lyrics;

/// <summary>
/// 内嵌歌词读取：从容器标签里取歌词原文，口径对齐忆潮的 MusicEmbeddedLyricReader。
///
/// 覆盖 MP3（ID3v2 的 USLT 帧）、FLAC 与 OGG（Vorbis 注释的 LYRICS/SYNCEDLYRICS/UNSYNCEDLYRICS）、
/// M4A（©lyr 原子或自由格式的 lyrics）。
/// 标签位于音频之后的容器（WAV 的 ID3 块）这一轮不做；取不到就按没有歌词处理，不猜测。
/// </summary>
public static class EmbeddedLyrics
{
    /// <summary>标签读取窗口：MP3 的标签含封面时会很大，其余容器的注释块都在文件头部附近。</summary>
    private const int HeaderCap = 4 * 1024 * 1024;

    private static readonly byte[] Mp4LyricAtom = { 0xA9, (byte)'l', (byte)'y', (byte)'r' };

    public static string? ReadText(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            var size = (int)Math.Min(HeaderCap, stream.Length);
            if (size <= 0)
            {
                return null;
            }

            var header = new byte[size];
            stream.Seek(0, SeekOrigin.Begin);
            var read = 0;
            while (read < size)
            {
                var count = stream.Read(header, read, size - read);
                if (count <= 0)
                {
                    break;
                }

                read += count;
            }

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => ReadId3Lyrics(header),
                ".flac" => ReadFlacLyrics(header),
                ".ogg" or ".opus" => ReadOggLyrics(header),
                ".m4a" or ".m4b" or ".mp4" => ReadMp4Lyrics(header),
                _ => null,
            };
        }
        catch (Exception)
        {
            // 占用、权限、结构不认识：按没有歌词处理，不影响播放
            return null;
        }
    }

    // ---- ID3v2（MP3）

    private static string? ReadId3Lyrics(byte[] data)
    {
        if (data.Length < 10
            || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        {
            return null;
        }

        var version = data[3];
        var flags = data[5];
        var tagSize = SyncSafe(data, 6);
        var end = (int)Math.Min(data.Length, 10 + tagSize);
        var position = 10;

        // 扩展头：v2.3 起才有，其长度为 4 字节，跳过
        if ((flags & 0x40) != 0 && version >= 3 && position + 4 <= end)
        {
            position += 4 + ReadInt32BigEndian(data, position);
        }

        var headerLength = version == 2 ? 6 : 10;
        while (position + headerLength <= end)
        {
            var id = version == 2
                ? Encoding.ASCII.GetString(data, position, 3)
                : Encoding.ASCII.GetString(data, position, 4);
            if (string.IsNullOrWhiteSpace(id.Replace("\0", string.Empty)))
            {
                break;
            }

            var size = version == 2
                ? (data[position + 3] << 16) | (data[position + 4] << 8) | data[position + 5]
                : version >= 4
                    ? SyncSafe(data, position + 4)
                    : ReadInt32BigEndian(data, position + 4);
            if (size <= 0 || position + headerLength + size > end)
            {
                break;
            }

            if (id is "USLT" or "ULT")
            {
                return DecodeUslt(data, position + headerLength, size);
            }

            position += headerLength + size;
        }

        return null;
    }

    /// <summary>USLT 帧体：1 字节编码 + 3 字节语言 + 内容描述（按编码零结尾）+ 歌词原文。</summary>
    private static string? DecodeUslt(byte[] data, int start, int length)
    {
        var end = start + length;
        if (start + 4 > end)
        {
            return null;
        }

        var encoding = data[start];
        var position = start + 4;
        position = encoding is 1 or 2
            ? SkipUtf16Terminator(data, position, end)
            : SkipByteTerminator(data, position, end);
        if (position < 0 || position >= end)
        {
            return null;
        }

        return DecodeText(data, position, end - position, encoding);
    }

    private static string? DecodeText(byte[] data, int offset, int length, byte encoding)
    {
        if (length <= 0)
        {
            return null;
        }

        var text = encoding switch
        {
            0 => Encoding.Latin1.GetString(data, offset, length),
            1 => Encoding.Unicode.GetString(TrimBom(data, offset, length, bigEndian: false)),
            2 => Encoding.BigEndianUnicode.GetString(data, offset, length),
            _ => Encoding.UTF8.GetString(data, offset, length),
        };
        return text.TrimEnd('\0').Trim() is { Length: > 0 } value ? value : null;
    }

    // ---- FLAC / OGG（Vorbis 注释）

    private static string? ReadFlacLyrics(byte[] data)
    {
        if (data.Length < 8 || data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
        {
            return null;
        }

        var position = 4;
        while (position + 4 <= data.Length)
        {
            var header = data[position];
            var type = header & 0x7F;
            var length = (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
            position += 4;

            if (type == 4 && position + length <= data.Length)
            {
                return ParseVorbisLyrics(data, position, length);
            }

            position += length;
            if ((header & 0x80) != 0)
            {
                break;
            }
        }

        return null;
    }

    private static string? ReadOggLyrics(byte[] data)
    {
        var opus = IndexOf(data, Encoding.ASCII.GetBytes("OpusTags"));
        if (opus >= 0)
        {
            var start = opus + 8;
            return ParseVorbisLyrics(data, start, data.Length - start);
        }

        var vorbis = IndexOf(data, new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
        if (vorbis >= 0)
        {
            var start = vorbis + 7;
            return ParseVorbisLyrics(data, start, data.Length - start);
        }

        return null;
    }

    /// <summary>Vorbis 注释：32 位小端 vendor 长度与内容、字段数，随后是「KEY=值」字段。</summary>
    private static string? ParseVorbisLyrics(byte[] data, int offset, int length)
    {
        var end = Math.Min(data.Length, offset + length);
        var position = offset;
        if (position + 4 > end)
        {
            return null;
        }

        var vendorLength = ReadInt32LittleEndian(data, position);
        if (vendorLength < 0 || position + 4 + vendorLength + 4 > end)
        {
            return null;
        }

        position += 4 + vendorLength;
        var count = ReadInt32LittleEndian(data, position);
        position += 4;

        string? fallback = null;
        for (var i = 0; i < count && position + 4 <= end; i++)
        {
            var valueLength = ReadInt32LittleEndian(data, position);
            position += 4;
            if (valueLength < 0 || position + valueLength > end)
            {
                break;
            }

            var entry = Encoding.UTF8.GetString(data, position, valueLength);
            position += valueLength;

            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = entry[..separator].ToUpperInvariant();
            var value = entry[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (key)
            {
                case "LYRICS":
                    return value;
                case "SYNCEDLYRICS":
                case "UNSYNCEDLYRICS":
                    fallback ??= value;
                    break;
            }
        }

        return fallback;
    }

    // ---- MP4（©lyr）

    /// <summary>©lyr 原子的载荷是 data 子原子，文本自 data 头之后 16 字节起。</summary>
    private static string? ReadMp4Lyrics(byte[] data)
    {
        for (var i = 4; i + 4 < data.Length; i++)
        {
            if (data[i] != Mp4LyricAtom[0] || data[i + 1] != Mp4LyricAtom[1]
                || data[i + 2] != Mp4LyricAtom[2] || data[i + 3] != Mp4LyricAtom[3])
            {
                continue;
            }

            var size = ReadInt32BigEndian(data, i - 4);
            var end = Math.Min(data.Length, i - 4 + Math.Max(size, 0));
            var text = FindDataAtomText(data, i + 4, end);
            if (text is not null)
            {
                return text;
            }
        }

        return null;
    }

    private static string? FindDataAtomText(byte[] data, int start, int end)
    {
        for (var p = start; p + 8 <= end; p++)
        {
            if (data[p] != 'd' || data[p + 1] != 'a' || data[p + 2] != 't' || data[p + 3] != 'a')
            {
                continue;
            }

            var size = ReadInt32BigEndian(data, p - 4);
            var textStart = p + 12;
            var textEnd = Math.Min(end, p - 4 + size);
            if (size <= 16 || textStart >= textEnd)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(data, textStart, textEnd - textStart).TrimEnd('\0').Trim();
            return text.Length > 0 ? text : null;
        }

        return null;
    }

    // ---- 基础

    private static int SyncSafe(byte[] data, int position) =>
        ((data[position] & 0x7F) << 21)
        | ((data[position + 1] & 0x7F) << 14)
        | ((data[position + 2] & 0x7F) << 7)
        | (data[position + 3] & 0x7F);

    private static int ReadInt32BigEndian(byte[] data, int position) =>
        (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];

    private static int ReadInt32LittleEndian(byte[] data, int position) =>
        data[position] | (data[position + 1] << 8) | (data[position + 2] << 16) | (data[position + 3] << 24);

    /// <summary>跳过单字节零结尾的描述段；找不到返回 -1。</summary>
    private static int SkipByteTerminator(byte[] data, int position, int end)
    {
        while (position < end && data[position] != 0)
        {
            position++;
        }

        return position < end ? position + 1 : -1;
    }

    /// <summary>跳过 UTF-16 的双零结尾：按两字节对齐找 0x0000。</summary>
    private static int SkipUtf16Terminator(byte[] data, int position, int end)
    {
        while (position + 1 < end)
        {
            if (data[position] == 0 && data[position + 1] == 0)
            {
                return position + 2;
            }

            position += 2;
        }

        return -1;
    }

    /// <summary>UTF-16 带 BOM 时去掉 BOM 再交解码器，否则 BOM 会变成一个可见字符。</summary>
    private static byte[] TrimBom(byte[] data, int offset, int length, bool bigEndian)
    {
        var hasBom = length >= 2
            && (bigEndian ? data[offset] == 0xFE && data[offset + 1] == 0xFF : data[offset] == 0xFF && data[offset + 1] == 0xFE);
        if (!hasBom)
        {
            return data[offset..(offset + length)];
        }

        return data[(offset + 2)..(offset + length)];
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }
}
