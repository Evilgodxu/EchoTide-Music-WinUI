using System;
using System.IO;
using System.Text;

namespace YiChao.Data.Metadata.Tags;

/// <summary>
/// WAV 标签与时长：fmt 块给出字节率，data 块的大小除以字节率即时长；
/// 标题等取自 LIST/INFO 块（INAM/IART/IPRD）。
/// </summary>
internal static class WavTags
{
    /// <summary>头部扫描上限：INFO 块通常紧跟在 fmt 之后，超出的部分不再找。</summary>
    private const int MaxHeaderBytes = 1024 * 1024;

    public static AudioTag Read(FileStream stream)
    {
        var header = AudioTagReader.ReadBytes(stream, 0, MaxHeaderBytes);
        if (header.Length < 12
            || header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F'
            || header[8] != 'W' || header[9] != 'A' || header[10] != 'V' || header[11] != 'E')
        {
            return default;
        }

        string? title = null, artist = null, album = null;
        var byteRate = 0;
        long dataSize = 0;
        var offset = 12;

        while (offset + 8 <= header.Length)
        {
            var id = Encoding.ASCII.GetString(header, offset, 4);
            var size = (long)ReadUInt32LittleEndian(header, offset + 4);
            var payloadStart = offset + 8;

            switch (id)
            {
                case "fmt " when payloadStart + 12 <= header.Length:
                    // 采样率在 +4，字节率在 +8
                    byteRate = (int)ReadUInt32LittleEndian(header, payloadStart + 8);
                    break;
                case "data":
                    // data 块可能超出已读窗口，大小以块头声明为准
                    dataSize = size;
                    break;
                case "LIST" when payloadStart + 4 <= header.Length:
                    ReadListInfo(header, payloadStart, Math.Min(header.Length, payloadStart + (int)size),
                        ref title, ref artist, ref album);
                    break;
            }

            if (dataSize > 0 && byteRate > 0)
            {
                break;
            }

            offset = payloadStart + (int)size + ((int)size & 1);
        }

        var duration = byteRate > 0 && dataSize > 0
            ? TimeSpan.FromSeconds((double)dataSize / byteRate)
            : TimeSpan.Zero;
        return new AudioTag(title, artist, album, duration);
    }

    private static void ReadListInfo(byte[] data, int start, int end, ref string? title, ref string? artist, ref string? album)
    {
        if (start + 4 > end || Encoding.ASCII.GetString(data, start, 4) != "INFO")
        {
            return;
        }

        var offset = start + 4;
        while (offset + 8 <= end)
        {
            var id = Encoding.ASCII.GetString(data, offset, 4);
            var size = (int)Math.Min(ReadUInt32LittleEndian(data, offset + 4), (uint)Math.Max(0, end - offset - 8));
            if (size < 0)
            {
                return;
            }

            var value = AudioTagReader.Normalize(Encoding.UTF8.GetString(data, offset + 8, size));
            switch (id)
            {
                case "INAM":
                    title ??= value;
                    break;
                case "IART":
                    artist ??= value;
                    break;
                case "IPRD":
                    album ??= value;
                    break;
            }

            offset += 8 + size + (size & 1);
        }
    }

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
}
