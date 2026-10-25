using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using YiChao.Ui.Component;

namespace YiChao.Data.Metadata;

/// <summary>封面取图结果：已带四边羽化的显示位图与背景取色。无封面时两者皆空。</summary>
public sealed record CoverArt(ImageSource? Image, CoverPalette? Palette);

/// <summary>
/// 封面取图与羽化：走系统媒体缩略图，与忆潮用系统缩略图取封面同一思路——
/// 交给平台的媒体索引给出尺寸档，省去自己解析 ID3/FLAC 内嵌图。
///
/// 四边羽化在解码后的像素上一次烘好：WinUI 3 没有 OpacityMask，也不支持按遮罩合成，
/// 与其引入图形依赖去做遮罩相乘，不如把两轴的 smootherstep 直接乘进 alpha。
/// 每首曲目只做一次，代价是一条 1024 见方的像素遍历。
/// </summary>
public static class CoverLoader
{
    /// <summary>缩略图像素小于该值即视为系统回落的类型图标，不作为封面显示。</summary>
    private const uint IconFallbackEdge = 48;

    /// <summary>解码档长边：足够铺满首页左栏，又不至于按原图分配内存。</summary>
    private const uint DecodeEdge = 1024;

    private const int ColorSampleEdge = 64;

    public static async Task<CoverArt?> LoadAsync(string filePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.MusicView,
                1024,
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            using var stream = thumbnail.CloneStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth <= IconFallbackEdge)
            {
                return new CoverArt(null, null);
            }

            var (pixels, width, height) = await DecodeAsync(decoder);

            // 取色用未羽化的像素：羽化带只是与背景的过渡，边缘环的主色不应被 alpha 影响
            var palette = CoverColorExtractor.Extract(pixels, width, height, width * 4);

            ApplyEdgeFeather(pixels, width, height);

            var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(pixels.AsBuffer());
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return new CoverArt(source, palette);
        }
        catch (Exception)
        {
            // 取图失败按无封面处理：背景退回该曲取色或默认渐变，不阻断播放
            return null;
        }
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> DecodeAsync(BitmapDecoder decoder)
    {
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        var transform = new BitmapTransform
        {
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        // 按长边封顶等比缩放，短边跟随，不改变封面比例
        if (Math.Max(width, height) > DecodeEdge)
        {
            var ratio = (double)DecodeEdge / Math.Max(width, height);
            transform.ScaledWidth = (uint)Math.Max(1, Math.Round(width * ratio));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Round(height * ratio));
        }

        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return (data.DetachPixelData(), transform.ScaledWidth == 0 ? width : (int)transform.ScaledWidth, transform.ScaledHeight == 0 ? height : (int)transform.ScaledHeight);
    }

    /// <summary>
    /// 四边羽化：左右与上下两轴的 alpha 相乘，得到四边同时渐隐、四角衰减更强的结果。
    /// 逐像素改写 alpha 时同步缩放 RGB——位图按预乘 alpha 存储，只改 alpha 会让边缘发亮。
    /// </summary>
    private static void ApplyEdgeFeather(byte[] pixels, int width, int height)
    {
        var band = Math.Max(1, (int)(Math.Min(width, height) * CoverFade.FadeRatio));
        for (var y = 0; y < height; y++)
        {
            var vertical = AxisFactor(y, height, band);
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var horizontal = AxisFactor(x, width, band);
                var factor = horizontal * vertical;
                if (factor >= 1)
                {
                    continue;
                }

                var offset = row + (x * 4);
                pixels[offset] = (byte)(pixels[offset] * factor);
                pixels[offset + 1] = (byte)(pixels[offset + 1] * factor);
                pixels[offset + 2] = (byte)(pixels[offset + 2] * factor);
                pixels[offset + 3] = (byte)(pixels[offset + 3] * factor);
            }
        }
    }

    /// <summary>轴向羽化系数：贴边为 0，进入羽化带内侧为 1，带外恒为 1。</summary>
    private static double AxisFactor(int position, int length, int band)
    {
        var fromStart = position;
        var fromEnd = length - 1 - position;
        var distance = Math.Min(fromStart, fromEnd);
        if (distance >= band)
        {
            return 1;
        }

        return CoverFade.SmoothFadeAlpha(1.0 - ((double)distance / band));
    }
}
