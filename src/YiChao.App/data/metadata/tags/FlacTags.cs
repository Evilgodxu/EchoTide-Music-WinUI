using System;
using System.IO;
using System.Text;

namespace YiChao.Data.Metadata.Tags;

/// <summary>
/// FLAC 标签与时长：STREAMINFO 给出精确的采样数与采样率，VorbisComment 给出标题/艺术家/专辑。
/// 两者都在文件头的元数据块里，读到第一个音频帧之前即可停止。
/// </summary>
internal static class FlacTags
{
    private const int StreamInfoBlock = 0;
    private const int VorbisCommentBlock = 4;

    public static AudioTag Read(FileStream stream)
    {
        var magic = AudioTagReader.ReadBytes(stream, 0, 4);
        if (magic.Length < 4 || magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C')
        {
            return default;
        }

        string? title = null, artist = null, album = null;
        var duration = TimeSpan.Zero;
        long offset = 4;

        while (true)
        {
            var header = AudioTagReader.ReadBytes(stream, offset, 4);
            if (header.Length < 4)
            {
                break;
            }

            var isLast = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7F;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0)
            {
                break;
            }

            // 只取用得上的两块，其余类型跳过不读
            if (type is StreamInfoBlock or VorbisCommentBlock && length is > 0 and <= 8 * 1024 * 1024)
            {
                var block = AudioTagReader.ReadBytes(stream, offset + 4, length);
                if (type == StreamInfoBlock)
                {
                    duration = ReadStreamInfo(block);
                }
                else
                {
                    ReadVorbisComment(block, ref title, ref artist, ref album);
                }
            }

            if (isLast)
            {
                break;
            }

            offset += 4 + length;
        }

        return new AudioTag(title, artist, album, duration);
    }

    /// <summary>STREAMINFO 尾部 8 字节打包了采样率、声道数、位深与总采样数。</summary>
    private static TimeSpan ReadStreamInfo(byte[] block)
    {
        if (block.Length < 18)
        {
            return TimeSpan.Zero;
        }

        var packed = (ulong)block[10] << 56 | (ulong)block[11] << 48 | (ulong)block[12] << 40 | (ulong)block[13] << 32
            | (ulong)block[14] << 24 | (ulong)block[15] << 16 | (ulong)block[16] << 8 | block[17];

        var sampleRate = (int)((packed >> 44) & 0xFFFFF);
        var totalSamples = (long)(packed & 0xFFFFFFFFF);
        if (sampleRate <= 0 || totalSamples <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds((double)totalSamples / sampleRate);
    }

    /// <summary>VorbisComment：厂商串在前，随后是「键=值」形式的字段列表，键名不区分大小写。</summary>
    private static void ReadVorbisComment(byte[] block, ref string? title, ref string? artist, ref string? album)
    {
        var offset = 0;

        if (!TryReadUInt32LittleEndian(block, ref offset, out var vendorLength))
        {
            return;
        }

        offset += (int)Math.Min(vendorLength, (uint)Math.Max(0, block.Length - offset));
        if (!TryReadUInt32LittleEndian(block, ref offset, out var fieldCount))
        {
            return;
        }

        var limit = Math.Min(fieldCount, 1024u);
        for (var i = 0u; i < limit; i++)
        {
            if (!TryReadUInt32LittleEndian(block, ref offset, out var fieldLength))
            {
                return;
            }

            var length = (int)Math.Min(fieldLength, (uint)Math.Max(0, block.Length - offset));
            if (length <= 0 || offset + length > block.Length)
            {
                return;
            }

            var field = Encoding.UTF8.GetString(block, offset, length);
            offset += length;

            var separator = field.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = field[..separator].ToUpperInvariant();
            var value = AudioTagReader.Normalize(field[(separator + 1)..]);
            switch (key)
            {
                case "TITLE":
                    title ??= value;
                    break;
                case "ARTIST":
                    artist ??= value;
                    break;
                case "ALBUM":
                    album ??= value;
                    break;
            }
        }
    }

    private static bool TryReadUInt32LittleEndian(byte[] data, ref int offset, out uint value)
    {
        value = 0;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        value = (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        offset += 4;
        return true;
    }
}
