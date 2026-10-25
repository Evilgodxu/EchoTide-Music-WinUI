using System;
using System.IO;
using System.Text;

namespace YiChao.Data.Metadata.Tags;

/// <summary>
/// MP3 标签与时长：读 ID3v2 的 TIT2/TPE1/TALB，时长按首个 MPEG 帧头推算。
///
/// 有时长优先取 Xing/Info 帧里的总帧数（VBR 才准），没有才按码率折算码流长度。
/// 只读文件头与标签块，不解码音频。
/// </summary>
internal static class Id3v2Tags
{
    private static readonly int[] BitratesV1Layer3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
    private static readonly int[] BitratesV2Layer3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
    private static readonly int[] SampleRatesV1 = { 44100, 48000, 32000, 0 };
    private static readonly int[] SampleRatesV2 = { 22050, 24000, 16000, 0 };
    private static readonly int[] SampleRatesV25 = { 11025, 12000, 8000, 0 };

    public static AudioTag Read(FileStream stream)
    {
        var header = AudioTagReader.ReadBytes(stream, 0, 10);
        var tagSize = 0;
        string? title = null, artist = null, album = null;

        if (header.Length == 10 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
        {
            var major = header[3];
            var flags = header[5];
            var declared = SyncSafe(header, 6);
            var hasFooter = (flags & 0x10) != 0;
            tagSize = 10 + declared + (hasFooter ? 10 : 0);

            var body = AudioTagReader.ReadBytes(stream, 10, Math.Min(declared, 1024 * 1024));
            if ((flags & 0x80) != 0)
            {
                body = RemoveUnsynchronisation(body);
            }

            ReadTextFrames(body, major, flags, ref title, ref artist, ref album);
        }

        var duration = ReadDuration(stream, tagSize);
        return new AudioTag(title, artist, album, duration);
    }

    private static void ReadTextFrames(byte[] body, int major, byte flags, ref string? title, ref string? artist, ref string? album)
    {
        var offset = 0;

        // 扩展头：v2.3 的长度字段不含自身，v2.4 含
        if ((flags & 0x40) != 0 && body.Length >= 4)
        {
            var extended = major >= 4 ? SyncSafe(body, 0) : (int)ReadUInt32(body, 0) + 4;
            offset = Math.Clamp(extended, 0, body.Length);
        }

        var idLength = major >= 3 ? 4 : 3;
        var headerLength = major >= 3 ? 10 : 6;

        while (offset + headerLength <= body.Length)
        {
            if (body[offset] == 0)
            {
                break;
            }

            var id = Encoding.ASCII.GetString(body, offset, idLength);
            var size = major >= 4 ? SyncSafe(body, offset + idLength) : (int)ReadUInt32(body, offset + idLength);
            if (size <= 0 || offset + headerLength + size > body.Length)
            {
                break;
            }

            var payload = new ReadOnlySpan<byte>(body, offset + headerLength, size);
            if (id is "TIT2" or "TPE1" or "TALB")
            {
                var text = DecodeText(payload);
                switch (id)
                {
                    case "TIT2":
                        title ??= text;
                        break;
                    case "TPE1":
                        artist ??= text;
                        break;
                    default:
                        album ??= text;
                        break;
                }
            }

            offset += headerLength + size;
        }
    }

    /// <summary>文本帧解码：首字节为编码标识，文本可能带 UTF-16 的 BOM。</summary>
    private static string? DecodeText(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }

        var encoding = payload[0];
        var content = payload[1..];
        return encoding switch
        {
            0 => AudioTagReader.Normalize(Encoding.Latin1.GetString(content)),
            1 => AudioTagReader.Normalize(Encoding.Unicode.GetString(content)),
            2 => AudioTagReader.Normalize(Encoding.BigEndianUnicode.GetString(content)),
            3 => AudioTagReader.Normalize(Encoding.UTF8.GetString(content)),
            _ => null,
        };
    }

    /// <summary>去掉 ID3v2.3 的整体非同步：0xFF 后插入的 0x00 需要剔除。</summary>
    private static byte[] RemoveUnsynchronisation(byte[] data)
    {
        var output = new byte[data.Length];
        var length = 0;
        for (var i = 0; i < data.Length; i++)
        {
            output[length++] = data[i];
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
            {
                i++;
            }
        }

        return output[..length];
    }

    /// <summary>时长：从标签之后找第一个 MPEG 帧头，优先读 Xing/Info 的总帧数。</summary>
    private static TimeSpan ReadDuration(FileStream stream, int tagSize)
    {
        var searchLength = (int)Math.Min(8 * 1024, Math.Max(0, stream.Length - tagSize));
        var buffer = AudioTagReader.ReadBytes(stream, tagSize, searchLength);
        if (buffer.Length < 4)
        {
            return TimeSpan.Zero;
        }

        for (var i = 0; i + 4 <= buffer.Length; i++)
        {
            if (buffer[i] != 0xFF || (buffer[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var header = ParseFrameHeader(buffer, i);
            if (header is null)
            {
                continue;
            }

            var frame = header.Value;
            var frames = ReadXingFrameCount(buffer, i, frame);
            if (frames is { } frameCount && frameCount > 0)
            {
                return TimeSpan.FromSeconds((double)frameCount * frame.SamplesPerFrame / frame.SampleRate);
            }

            var audioBytes = stream.Length - tagSize - i;
            if (HasId3v1(stream))
            {
                audioBytes -= 128;
            }

            if (frame.BitrateKbps <= 0 || audioBytes <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(audioBytes * 8.0 / (frame.BitrateKbps * 1000.0));
        }

        return TimeSpan.Zero;
    }

    private readonly record struct FrameHeader(int BitrateKbps, int SampleRate, int SamplesPerFrame, int SideInfoSize);

    private static FrameHeader? ParseFrameHeader(byte[] buffer, int offset)
    {
        if (offset + 4 > buffer.Length)
        {
            return null;
        }

        var b1 = buffer[offset + 1];
        var b2 = buffer[offset + 2];
        var b3 = buffer[offset + 3];

        var versionBits = (b1 >> 3) & 0x03;
        var layerBits = (b1 >> 1) & 0x03;
        var bitrateIndex = (b2 >> 4) & 0x0F;
        var sampleRateIndex = (b2 >> 2) & 0x03;
        var channelMode = (b3 >> 6) & 0x03;

        // 只处理 Layer III：曲库里几乎只有这一种，其余按无法判定处理
        if (layerBits != 0x01 || bitrateIndex == 0 || bitrateIndex == 0x0F || sampleRateIndex == 0x03)
        {
            return null;
        }

        var isMpeg1 = versionBits == 0x03;
        var sampleRate = versionBits switch
        {
            0x03 => SampleRatesV1[sampleRateIndex],
            0x02 => SampleRatesV2[sampleRateIndex],
            0x00 => SampleRatesV25[sampleRateIndex],
            _ => 0,
        };
        if (sampleRate == 0)
        {
            return null;
        }

        var bitrate = isMpeg1 ? BitratesV1Layer3[bitrateIndex] : BitratesV2Layer3[bitrateIndex];
        var samplesPerFrame = isMpeg1 ? 1152 : 576;
        var mono = channelMode == 0x03;
        var sideInfo = isMpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17);

        return new FrameHeader(bitrate, sampleRate, samplesPerFrame, sideInfo);
    }

    private static int? ReadXingFrameCount(byte[] buffer, int frameOffset, FrameHeader frame)
    {
        var xingOffset = frameOffset + 4 + frame.SideInfoSize;
        if (xingOffset + 12 > buffer.Length)
        {
            return null;
        }

        var tag = Encoding.ASCII.GetString(buffer, xingOffset, 4);
        if (tag is not ("Xing" or "Info"))
        {
            return null;
        }

        var flags = ReadUInt32(buffer, xingOffset + 4);
        if ((flags & 0x1) == 0)
        {
            return null;
        }

        return (int)Math.Min(ReadUInt32(buffer, xingOffset + 8), int.MaxValue);
    }

    private static bool HasId3v1(FileStream stream) =>
        stream.Length > 128 && AudioTagReader.ReadBytes(stream, stream.Length - 128, 3) is { Length: 3 } tag
        && tag[0] == 'T' && tag[1] == 'A' && tag[2] == 'G';

    private static int SyncSafe(byte[] bytes, int offset) =>
        ((bytes[offset] & 0x7F) << 21)
        | ((bytes[offset + 1] & 0x7F) << 14)
        | ((bytes[offset + 2] & 0x7F) << 7)
        | (bytes[offset + 3] & 0x7F);

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
}
