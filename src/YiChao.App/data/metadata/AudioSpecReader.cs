using System;
using System.Globalization;
using System.IO;

namespace YiChao.Data.Metadata;

/// <summary>
/// 当前曲目的音频规格：格式名取自扩展名，位深与采样率取自容器头，比特率按文件大小与时长折算。
///
/// 只在切到某首曲目时现读一次头，不进扫描期——曲库里几百首不会为此多花一遍 IO。
/// </summary>
public readonly record struct AudioSpec(string Format, int? BitDepth, int? SampleRate, int? BitrateKbps)
{
    /// <summary>「格式 · 位深/采样率 · 比特率」；缺哪项就少哪项，全缺时为空串。</summary>
    public string Label
    {
        get
        {
            var spec = BuildSpec();
            var bitrate = BitrateKbps is int kbps ? $"{kbps}kbps" : null;
            var parts = new[] { Format, spec, bitrate };
            return string.Join(" · ", Array.FindAll(parts, part => !string.IsNullOrEmpty(part)));
        }
    }

    private string? BuildSpec()
    {
        if (SampleRate is int rate && BitDepth is int depth)
        {
            return $"{depth}bit/{FormatKhz(rate)}kHz";
        }

        if (SampleRate is int onlyRate)
        {
            return $"{FormatKhz(onlyRate)}kHz";
        }

        return BitDepth is int onlyDepth ? $"{onlyDepth}bit" : null;
    }

    /// <summary>整千赫不带小数点，非整千保留一位。</summary>
    private static string FormatKhz(int rate) =>
        (rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>
/// 音频规格读取：按容器各读一小段头。读不到的项留空，不猜测。
///
/// 比特率取平均——解码路径能报出的瞬时码率与曲目的名义码率并不总是同一个值，
/// 平均码率与文件本身对得上，也不依赖具体解码器。
/// </summary>
public static class AudioSpecReader
{
    /// <summary>头部读取窗口：MP4 的 stsd 在 moov 里，较大的封面会把 moov 推到几百 KB 之后。</summary>
    private const int HeaderBytes = 512 * 1024;

    private static readonly int[] Mpeg1Layer3Bitrates =
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };

    private static readonly int[] Mpeg2Layer3Bitrates =
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

    public static AudioSpec Read(string path, TimeSpan duration)
    {
        var format = FormatName(path);
        int? sampleRate = null;
        int? bitDepth = null;
        int? declaredBitrate = null;
        long length = 0;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            length = stream.Length;
            var header = ReadHeader(stream);

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".flac":
                    (sampleRate, bitDepth) = ReadFlac(header);
                    break;
                case ".wav":
                    (sampleRate, bitDepth) = ReadWav(header);
                    break;
                case ".mp3":
                    (sampleRate, declaredBitrate) = ReadMpegFrame(header);
                    break;
                case ".m4a" or ".m4b" or ".mp4":
                    (sampleRate, bitDepth) = ReadMp4(header);
                    break;
            }
        }
        catch (Exception)
        {
            // 文件正被占用或格式不认识：信息条退到只显示格式，不阻断播放
        }

        return new AudioSpec(format, bitDepth, sampleRate, declaredBitrate ?? AverageBitrate(length, duration));
    }

    /// <summary>扩展名转格式名，与曲库白名单对得上；认不出的按大写扩展名原样显示。</summary>
    private static string FormatName(string path) =>
        Path.GetExtension(path).TrimStart('.').ToUpperInvariant() switch
        {
            "" => "AUDIO",
            var name => name,
        };

    private static byte[] ReadHeader(FileStream stream)
    {
        var size = (int)Math.Min(HeaderBytes, stream.Length);
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[size];
        stream.Seek(0, SeekOrigin.Begin);
        var read = 0;
        while (read < size)
        {
            var count = stream.Read(buffer, read, size - read);
            if (count <= 0)
            {
                break;
            }

            read += count;
        }

        return read == size ? buffer : buffer[..read];
    }

    private static int? AverageBitrate(long bytes, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || bytes <= 0)
        {
            return null;
        }

        // 折算结果不足 1kbps 的多是极短样本，报出来只是噪音
        var kbps = (int)Math.Round(bytes * 8 / duration.TotalSeconds / 1000);
        return kbps > 0 ? kbps : null;
    }

    /// <summary>FLAC 的 STREAMINFO 固定是第一个元数据块：4 字节标识 + 4 字节块头之后取规格。</summary>
    private static (int? SampleRate, int? BitDepth) ReadFlac(byte[] data)
    {
        const int payload = 8;
        const int spec = payload + 10;
        if (data.Length < spec + 4
            || data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
        {
            return default;
        }

        // 20 位采样率、3 位声道数、5 位位深紧邻排布：位深跨第 12、13 字节，占后者的高 4 位
        var sampleRate = (data[spec] << 12) | (data[spec + 1] << 4) | (data[spec + 2] >> 4);
        var bitDepth = (((data[spec + 2] & 0x01) << 4) | (data[spec + 3] >> 4)) + 1;
        return (sampleRate > 0 ? sampleRate : null, bitDepth);
    }

    /// <summary>WAV 取 fmt 块：采样率在负载 +4，位深在 +14。</summary>
    private static (int? SampleRate, int? BitDepth) ReadWav(byte[] data)
    {
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            if (data[offset] == 'f' && data[offset + 1] == 'm'
                && data[offset + 2] == 't' && data[offset + 3] == ' ')
            {
                var payload = offset + 8;
                if (payload + 16 > data.Length)
                {
                    return default;
                }

                var sampleRate = BitConverter.ToInt32(data, payload + 4);
                var bitDepth = BitConverter.ToInt16(data, payload + 14);
                return (sampleRate > 0 ? sampleRate : null, bitDepth > 0 ? bitDepth : null);
            }

            var size = BitConverter.ToInt32(data, offset + 4);
            if (size <= 0)
            {
                break;
            }

            // 块按偶数字节对齐，奇数长度后面有一个填充字节
            offset += 8 + size + (size & 1);
        }

        return default;
    }

    /// <summary>MP3：跳过 ID3v2 后找第一帧，取 MPEG 版本对应的采样率与名义码率。</summary>
    private static (int? SampleRate, int? BitrateKbps) ReadMpegFrame(byte[] data)
    {
        var start = 0;
        if (data.Length > 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            start = 10
                + ((data[6] & 0x7F) << 21)
                + ((data[7] & 0x7F) << 14)
                + ((data[8] & 0x7F) << 7)
                + (data[9] & 0x7F);
            if ((data[5] & 0x10) != 0)
            {
                start += 10;
            }
        }

        for (var i = Math.Max(0, start); i + 4 <= data.Length; i++)
        {
            if (data[i] != 0xFF || (data[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var version = (data[i + 1] >> 3) & 0x03;
            var layer = (data[i + 1] >> 1) & 0x03;
            var bitrateIndex = (data[i + 2] >> 4) & 0x0F;
            var rateIndex = (data[i + 2] >> 2) & 0x03;
            if (layer != 0x01 || bitrateIndex is 0 or 0x0F || rateIndex == 0x03)
            {
                // 只认 Layer III；码率表也是按 Layer III 给的
                continue;
            }

            var rates = version switch
            {
                0x03 => new[] { 44100, 48000, 32000 },
                0x02 => new[] { 22050, 24000, 16000 },
                0x00 => new[] { 11025, 12000, 8000 },
                _ => Array.Empty<int>(),
            };
            if (rates.Length == 0)
            {
                continue;
            }

            var bitrates = version == 0x03 ? Mpeg1Layer3Bitrates : Mpeg2Layer3Bitrates;
            return (rates[rateIndex], bitrates[bitrateIndex]);
        }

        return default;
    }

    /// <summary>MP4 取 stsd 里的 mp4a 条目：位深在类型后 +22，采样率在 +28（16.16 定点）。</summary>
    private static (int? SampleRate, int? BitDepth) ReadMp4(byte[] data)
    {
        for (var i = 4; i + 32 <= data.Length; i++)
        {
            if (data[i] != 'm' || data[i + 1] != 'p' || data[i + 2] != '4' || data[i + 3] != 'a')
            {
                continue;
            }

            var bitDepth = (data[i + 22] << 8) | data[i + 23];
            var sampleRate = (data[i + 28] << 8) | data[i + 29];
            if (sampleRate > 0)
            {
                return (sampleRate, bitDepth > 0 ? bitDepth : null);
            }
        }

        return default;
    }
}
