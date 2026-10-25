using System;
using System.IO;

namespace YiChao.Data.Metadata.Tags;

/// <summary>
/// 一首曲目的标签读取结果。任一字段为空表示标签里确实没有，调用方自行兜底。
/// </summary>
public readonly record struct AudioTag(string? Title, string? Artist, string? Album, TimeSpan Duration);

/// <summary>
/// 音频标签读取：按容器分派到各自的解析器，只读文件头与标签块，不做解码。
///
/// 不逐首走 Shell 属性系统：那条路每次取属性都要经 Shell 打开并解析一遍文件，
/// 实测约 2~3ms/首，且强依赖 Windows Search 索引是否收录该目录——索引未收录时退化成目录遍历。
/// 自己读标签是纯字节解析，单首在百微秒量级，与目录是否被索引无关。
/// 解析失败按「标签缺失」处理，仍以文件名入曲库，不影响播放。
/// </summary>
public static class AudioTagReader
{
    /// <summary>标签块读取上限：ID3v2 与 Vorbis 注释都远小于此，超出部分不是标签。</summary>
    private const int MaxTagBytes = 4 * 1024 * 1024;

    public static bool TryRead(string path, out AudioTag tag)
    {
        tag = default;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            var extension = Path.GetExtension(path);
            tag = extension.ToLowerInvariant() switch
            {
                ".flac" => FlacTags.Read(stream),
                ".m4a" or ".m4b" or ".mp4" => Mp4Tags.Read(stream),
                ".wav" => WavTags.Read(stream),
                ".mp3" => Id3v2Tags.Read(stream),
                _ => Id3v2Tags.Read(stream),
            };
            return true;
        }
        catch (Exception)
        {
            // 权限、坏文件、读到半截都不该让整次扫描失败
            return false;
        }
    }

    /// <summary>把流的一段读满，读到文件尾即返回实际长度。</summary>
    internal static byte[] ReadBytes(Stream stream, long offset, int length)
    {
        var available = Math.Max(0, stream.Length - offset);
        var size = (int)Math.Min(length, available);
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[size];
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

    /// <summary>标签文本清理：去掉结尾的空字符与首尾空白，空串归为空引用。</summary>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Replace("\0", string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
