using System;
using System.IO;
using System.Text;

namespace YiChao.Data.Metadata.Tags;

/// <summary>
/// MP4/M4A 标签与时长：在 moov 里取 mvhd 的时长，在 udta/meta/ilst 里取标题、艺术家、专辑。
///
/// 只读 moov 一个原子（通常几十 KB，曲库元数据都在其中），mdat 一律跳过。
/// </summary>
internal static class Mp4Tags
{
    private const int MaxMoovBytes = 32 * 1024 * 1024;

    public static AudioTag Read(FileStream stream)
    {
        var moov = FindAtom(stream, "moov");
        if (moov is null)
        {
            return default;
        }

        var builder = new TagBuilder();
        var duration = TimeSpan.Zero;

        Walk(moov, 0, moov.Length, (type, start, end) =>
        {
            switch (type)
            {
                case "mvhd":
                    duration = ReadMovieHeader(moov, start, end);
                    break;
                case "udta":
                    ReadUserData(moov, start, end, builder);
                    break;
            }
        });

        return new AudioTag(builder.Title, builder.Artist, builder.Album, duration);
    }

    private static byte[]? FindAtom(FileStream stream, string target)
    {
        long position = 0;
        while (position + 8 <= stream.Length)
        {
            var head = AudioTagReader.ReadBytes(stream, position, 16);
            if (head.Length < 8)
            {
                return null;
            }

            var size = ReadUInt32BigEndian(head, 0);
            var type = Encoding.ASCII.GetString(head, 4, 4);
            long atomSize = size switch
            {
                0 => stream.Length - position,
                1 when head.Length >= 16 => (long)ReadUInt64BigEndian(head, 8),
                _ => size,
            };

            if (atomSize < 8)
            {
                return null;
            }

            if (type == target)
            {
                return AudioTagReader.ReadBytes(stream, position, (int)Math.Min(atomSize, MaxMoovBytes));
            }

            position += atomSize;
        }

        return null;
    }

    /// <summary>遍历一个原子容器内的同级子原子，把类型与负载区间交给调用方。</summary>
    private static void Walk(byte[] data, int start, int end, Action<string, int, int> visit)
    {
        var offset = start;
        while (offset + 8 <= end)
        {
            var size = ReadUInt32BigEndian(data, offset);
            var type = Encoding.ASCII.GetString(data, offset + 4, 4);
            var headerSize = 8;
            long atomSize = size;
            if (size == 1 && offset + 16 <= end)
            {
                atomSize = (long)ReadUInt64BigEndian(data, offset + 8);
                headerSize = 16;
            }
            else if (size == 0)
            {
                atomSize = end - offset;
            }

            if (atomSize < headerSize || offset + atomSize > end)
            {
                return;
            }

            visit(type, offset + headerSize, (int)(offset + atomSize));
            offset += (int)atomSize;
        }
    }

    private static TimeSpan ReadMovieHeader(byte[] data, int start, int end)
    {
        if (start + 4 > end)
        {
            return TimeSpan.Zero;
        }

        var version = data[start];
        // v0：两张 32 位时间戳后是 timescale 与 duration；v1：两张 64 位
        var offset = start + 4 + (version == 1 ? 16 : 8);
        if (offset + 8 > end)
        {
            return TimeSpan.Zero;
        }

        var timescale = ReadUInt32BigEndian(data, offset);
        var duration = version == 1
            ? (double)ReadUInt64BigEndian(data, offset + 4)
            : ReadUInt32BigEndian(data, offset + 4);
        if (timescale == 0 || duration <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(duration / timescale);
    }

    private static void ReadUserData(byte[] data, int start, int end, TagBuilder builder)
    {
        Walk(data, start, end, (type, childStart, childEnd) =>
        {
            if (type != "meta")
            {
                return;
            }

            // meta 是「完整原子」：负载前 4 字节是 version + flags，子原子从其后开始
            Walk(data, childStart + 4, childEnd, (metaType, listStart, listEnd) =>
            {
                if (metaType == "ilst")
                {
                    ReadItemList(data, listStart, listEnd, builder);
                }
            });
        });
    }

    private static void ReadItemList(byte[] data, int start, int end, TagBuilder builder)
    {
        Walk(data, start, end, (type, itemStart, itemEnd) =>
        {
            string? value = null;
            Walk(data, itemStart, itemEnd, (dataType, valueStart, valueEnd) =>
            {
                if (dataType != "data")
                {
                    return;
                }

                // data 原子：version + flags(4) + locale(4)，其后才是文本
                var textStart = valueStart + 8;
                if (textStart < valueEnd)
                {
                    value = AudioTagReader.Normalize(Encoding.UTF8.GetString(data, textStart, valueEnd - textStart));
                }
            });

            if (value is null)
            {
                return;
            }

            switch (type)
            {
                case "\u00A9nam":
                    builder.Title ??= value;
                    break;
                case "\u00A9ART":
                    builder.Artist ??= value;
                    break;
                case "\u00A9alb":
                    builder.Album ??= value;
                    break;
            }
        });
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
        (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);

    private static ulong ReadUInt64BigEndian(byte[] bytes, int offset) =>
        (ulong)ReadUInt32BigEndian(bytes, offset) << 32 | ReadUInt32BigEndian(bytes, offset + 4);

    /// <summary>原子遍历的写入端：lambda 里不能带 ref，用一个可变载体承接三段文本。</summary>
    private sealed class TagBuilder
    {
        public string? Title { get; set; }

        public string? Artist { get; set; }

        public string? Album { get; set; }
    }
}
