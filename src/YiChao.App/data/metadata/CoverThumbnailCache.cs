using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace YiChao.Data.Metadata;

/// <summary>
/// 列表行封面的缩略图缓存，口径照忆潮：内存 LRU + 单飞取图 + 无封面跳过标记。
///
/// 尺寸参与缓存键——解码结果随请求尺寸变化，不同档位不能互相顶替；
/// 请求尺寸由显示尺寸向上取到 32 的整数倍（见 <see cref="RequestSize"/>），
/// 同一首歌不会因所在行的封面差几个像素被重复解码。
///
/// 与首页大封面（<see cref="CoverLoader"/>）是两条口径：那边要羽化与取色、每首只取一次；
/// 这边只要一张小图，靠内存驻留承接反复滚动，不做任何落盘。
/// </summary>
public static class CoverThumbnailCache
{
    /// <summary>缩略图像素小于该值即视为系统回落的类型图标，不作为封面收录。</summary>
    private const int IconFallbackEdge = 40;

    /// <summary>分档步长：请求尺寸取到它的整数倍，保持为随显示尺寸变化的粗档位。</summary>
    private const int SizeStep = 32;

    /// <summary>驻留预算：64 见方的单张约 16KB，这个额度够装下几千张。</summary>
    private const long BudgetBytes = 128L * 1024 * 1024;

    private sealed record Entry(string Path, int SizePx, ImageSource Source, long Bytes);

    private static readonly object Gate = new();

    private static readonly Dictionary<(string Path, int SizePx), LinkedListNode<Entry>> Index = new();

    /// <summary>按最近使用排序：命中即提到最前，超预算从尾部淘汰。</summary>
    private static readonly LinkedList<Entry> Recency = new();

    /// <summary>在飞的取图：同一首歌被行与预取同时请求时只解码一次。</summary>
    private static readonly Dictionary<(string Path, int SizePx), Task<ImageSource?>> Inflight = new();

    /// <summary>已判定取不到封面的曲目：反复滚动同一首时不再重复查系统缩略图。</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase);

    private static DispatcherQueue? _dispatcher;

    private static long _bytes;

    /// <summary>按显示边长换算请求边长：向上取到 32 的整数倍，保证不低于显示尺寸。</summary>
    public static int RequestSize(int displayPx) =>
        Math.Max(1, (int)Math.Ceiling(displayPx / (double)SizeStep)) * SizeStep;

    /// <summary>
    /// 内存命中查询：显示端首帧同步取用它，命中就不必先闪一次占位。
    /// 供行与预取共用，两者判定必须一致，否则预取会空转。
    /// </summary>
    public static ImageSource? Peek(string path, int sizePx)
    {
        lock (Gate)
        {
            if (!Index.TryGetValue((path, sizePx), out var node))
            {
                return null;
            }

            Recency.Remove(node);
            Recency.AddFirst(node);
            return node.Value.Source;
        }
    }

    /// <summary>取一张缩略图：命中即返回，未命中则解码并回填；取不到返回 null 并记入跳过。</summary>
    public static Task<ImageSource?> LoadAsync(string path, int sizePx)
    {
        var key = (path, sizePx);
        lock (Gate)
        {
            if (Index.TryGetValue(key, out var node))
            {
                Recency.Remove(node);
                Recency.AddFirst(node);
                return Task.FromResult<ImageSource?>(node.Value.Source);
            }

            if (Skipped.Contains(path))
            {
                return Task.FromResult<ImageSource?>(null);
            }

            if (Inflight.TryGetValue(key, out var running))
            {
                return running;
            }

            var task = DecodeAndStoreAsync(key);
            Inflight[key] = task;
            return task;
        }
    }

    private static async Task<ImageSource?> DecodeAndStoreAsync((string Path, int SizePx) key)
    {
        try
        {
            var (source, bytes) = await DecodeAsync(key.Path, key.SizePx);
            if (source is null)
            {
                lock (Gate)
                {
                    Skipped.Add(key.Path);
                }

                return null;
            }

            Store(key, source, bytes);
            return source;
        }
        catch (Exception)
        {
            // 文件被占用、格式不认识：记入跳过，滚动到这一行时不再反复尝试
            lock (Gate)
            {
                Skipped.Add(key.Path);
            }

            return null;
        }
        finally
        {
            lock (Gate)
            {
                Inflight.Remove(key);
            }
        }
    }

    private static void Store((string Path, int SizePx) key, ImageSource source, long bytes)
    {
        lock (Gate)
        {
            if (Index.TryGetValue(key, out var existing))
            {
                _bytes -= existing.Value.Bytes;
                Recency.Remove(existing);
            }

            var node = Recency.AddFirst(new Entry(key.Path, key.SizePx, source, bytes));
            Index[key] = node;
            _bytes += bytes;

            while (_bytes > BudgetBytes && Recency.Last is { } last)
            {
                Recency.RemoveLast();
                Index.Remove((last.Value.Path, last.Value.SizePx));
                _bytes -= last.Value.Bytes;
            }
        }
    }

    private static async Task<(ImageSource? Source, long Bytes)> DecodeAsync(string path, int sizePx)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var thumbnail = await file.GetThumbnailAsync(
            ThumbnailMode.MusicView,
            (uint)sizePx,
            ThumbnailOptions.UseCurrentScale);
        if (thumbnail is null || thumbnail.Size == 0)
        {
            return (null, 0);
        }

        using var stream = thumbnail.CloneStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth <= IconFallbackEdge)
        {
            return (null, 0);
        }

        // 按长边封顶等比缩放，短边跟随：系统缩略图未必按请求尺寸给图
        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        if (Math.Max(width, height) > sizePx)
        {
            var ratio = (double)sizePx / Math.Max(width, height);
            width = Math.Max(1, (int)Math.Round(width * ratio));
            height = Math.Max(1, (int)Math.Round(height * ratio));
            transform.ScaledWidth = (uint)width;
            transform.ScaledHeight = (uint)height;
        }

        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var source = await RunOnUiAsync(() => CreateSourceAsync(data.DetachPixelData(), width, height));
        return (source, (long)width * height * 4);
    }

    /// <summary>SoftwareBitmapSource 是 XAML 对象，只能在界面线程上建立。</summary>
    private static Task<ImageSource?> RunOnUiAsync(Func<Task<ImageSource?>> work)
    {
        var dispatcher = _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            return work();
        }

        var completion = new TaskCompletionSource<ImageSource?>();
        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await work());
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
        });
        return completion.Task;
    }

    private static async Task<ImageSource?> CreateSourceAsync(byte[] pixels, int width, int height)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }
}
