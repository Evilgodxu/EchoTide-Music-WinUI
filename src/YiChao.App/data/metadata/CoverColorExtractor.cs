using System;
using Windows.UI;

namespace YiChao.Data.Metadata;

/// <summary>
/// 封面边缘环自上而下的三段色。三段已收敛为相近色，铺成竖向渐变时衔接处不出现跳色。
/// </summary>
public sealed record CoverPalette(Color Top, Color Middle, Color Bottom)
{
    /// <summary>代表色：三段相近，取中段供浮层底色与单色兜底复用。</summary>
    public Color Representative => Middle;

    public Color[] Stops => new[] { Top, Middle, Bottom };
}

/// <summary>
/// 封面取色：只采封面四周的边缘环，按纵向等分三段，每段取该段的主色。
///
/// 取样限定在外缘，因为背景正是与封面外缘相接的一片，取外缘色调才能与羽化边缘自然衔接。
/// 每段取主色用「量化直方图 + 择优」而非段内算术平均：平均会把互补色相相互抵消、饱和度塌成灰，
/// 边缘环里的黑边与白底会把平均色一点点拖向灰。量化剥掉低位噪声后取出现最多、最有彩度的一簇，
/// 色调才贴合实际观感。
///
/// 与 Android 端 <c>CoverBackgroundColor.kt</c> 的差异：那边用中位切分（median cut）切八簇再择优，
/// 这里是单层量化直方图直接在桶上择优。取色结果在常见封面上一致，代码量小一个数量级；
/// 若日后发现渐变偏灰，再补切分。
/// </summary>
public static class CoverColorExtractor
{
    private const double EdgeBandRatio = 0.14;
    private const int SegmentCount = 3;

    // 每通道量化到 32 档：低 3 位是压缩噪声，切掉后同一片颜色才落进同一个桶
    private const int QuantizeShift = 3;
    private const int Levels = 1 << (8 - QuantizeShift);

    private const double ScorePopulationWeight = 0.36;
    private const double ScoreSaturationWeight = 0.44;
    private const double ScoreLightnessWeight = 0.20;
    private const double PreferredLightness = 0.5;

    private const double HueToleranceDegrees = 20;
    private const double LightnessTolerance = 0.1;
    private const double LightnessMinSpread = 0.06;

    private const int OpaqueAlphaMin = 128;

    /// <summary>
    /// 从 BGRA8 像素缓冲取色。缓冲尺寸应已降到 64 长边左右——边缘环主色在那一档已稳定，
    /// 全尺寸封面按像素数分配数组毫无必要。
    /// </summary>
    public static CoverPalette? Extract(byte[] bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var band = Math.Max(1, (int)(Math.Min(width, height) * EdgeBandRatio));
        var sums = new long[SegmentCount, 3];
        var counts = new int[SegmentCount];
        var bins = new System.Collections.Generic.Dictionary<int, BinAccumulator>[SegmentCount];
        for (var i = 0; i < SegmentCount; i++)
        {
            bins[i] = new System.Collections.Generic.Dictionary<int, BinAccumulator>();
        }

        for (var y = 0; y < height; y++)
        {
            var segment = Math.Min(y * SegmentCount / height, SegmentCount - 1);
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                // 行列同时落在内圈即非边缘
                if (x >= band && x < width - band && y >= band && y < height - band)
                {
                    continue;
                }

                var offset = row + (x * 4);
                var b = bgra[offset];
                var g = bgra[offset + 1];
                var r = bgra[offset + 2];
                var a = bgra[offset + 3];
                if (a < OpaqueAlphaMin)
                {
                    continue;
                }

                counts[segment]++;
                sums[segment, 0] += r;
                sums[segment, 1] += g;
                sums[segment, 2] += b;

                var key = ((r >> QuantizeShift) * Levels + (g >> QuantizeShift)) * Levels + (b >> QuantizeShift);
                if (!bins[segment].TryGetValue(key, out var bin))
                {
                    bin = default;
                }

                bin.Add(r, g, b);
                bins[segment][key] = bin;
            }
        }

        var picked = new Color?[SegmentCount];
        for (var i = 0; i < SegmentCount; i++)
        {
            picked[i] = PickDominant(bins[i], counts[i], out var mean);
            // 段内平均色带不住色彩时本段判为无彩：色相与饱和度都不采纳，只留明度，
            // 底色因此是中性灰而不是偏色
            if (picked[i] is { } color && mean is { } meanColor && !IsChromatic(meanColor))
            {
                var (_, _, lightness) = ToHsl(color);
                picked[i] = FromHsl(0, 0, lightness);
            }
        }

        var available = Array.FindAll(picked, c => c.HasValue);
        if (available.Length == 0)
        {
            return null;
        }

        return Harmonize(picked);
    }

    private static Color? PickDominant(
        System.Collections.Generic.Dictionary<int, BinAccumulator> hist,
        int total,
        out Color? mean)
    {
        mean = null;
        if (total == 0 || hist.Count == 0)
        {
            return null;
        }

        long sumR = 0, sumG = 0, sumB = 0;
        Color? best = null;
        var bestScore = double.MinValue;
        foreach (var bin in hist.Values)
        {
            var color = bin.Average();
            sumR += bin.SumR;
            sumG += bin.SumG;
            sumB += bin.SumB;

            var (_, saturation, lightness) = ToHsl(color);
            var population = (double)bin.Count / total;
            var score = (ScorePopulationWeight * population)
                + (ScoreSaturationWeight * saturation)
                + (ScoreLightnessWeight * (1.0 - (Math.Abs(lightness - PreferredLightness) * 2.0)));
            if (score > bestScore)
            {
                bestScore = score;
                best = color;
            }
        }

        mean = Color.FromArgb(255, (byte)(sumR / total), (byte)(sumG / total), (byte)(sumB / total));
        return best;
    }

    /// <summary>三段色向中段收敛色相与明度，并按最小明度差拉开层级。</summary>
    private static CoverPalette Harmonize(Color?[] picked)
    {
        // 基准色取已取到色的中间那一段，缺失时取第一个可用段
        var reference = picked[1] ?? Array.Find(picked, c => c.HasValue)!.Value;
        var (refHue, refSaturation, refLightness) = ToHsl(reference);

        var hues = new double[SegmentCount];
        var sats = new double[SegmentCount];
        var lights = new double[SegmentCount];
        for (var i = 0; i < SegmentCount; i++)
        {
            var color = picked[i] ?? reference;
            var (h, s, l) = ToHsl(color);
            hues[i] = ConvergeHue(h, refHue);
            sats[i] = Math.Max(s, refSaturation * 0.85);
            // 明度先向基准收敛，再按段序拉开最小差值
            lights[i] = refLightness + Math.Clamp(l - refLightness, -LightnessTolerance, LightnessTolerance);
        }

        lights[0] = Math.Clamp(lights[0] - (LightnessMinSpread / 2), 0.04, 0.96);
        lights[1] = Math.Clamp(lights[1], 0.04, 0.96);
        lights[2] = Math.Clamp(lights[2] + (LightnessMinSpread / 2), 0.04, 0.96);

        return new CoverPalette(
            FromHsl(hues[0], sats[0], lights[0]),
            FromHsl(hues[1], sats[1], lights[1]),
            FromHsl(hues[2], sats[2], lights[2]));
    }

    private static double ConvergeHue(double hue, double reference)
    {
        var delta = hue - reference;
        while (delta > 180)
        {
            delta -= 360;
        }

        while (delta < -180)
        {
            delta += 360;
        }

        var clamped = Math.Clamp(delta, -HueToleranceDegrees, HueToleranceDegrees);
        var result = reference + clamped;
        return result < 0 ? result + 360 : result % 360;
    }

    private static bool IsChromatic(Color color)
    {
        var (_, saturation, _) = ToHsl(color);
        return saturation >= 0.08;
    }

    public static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2.0;
        if (max - min < 1e-6)
        {
            return (0, 0, lightness);
        }

        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
        {
            hue = ((g - b) / delta) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2;
        }
        else
        {
            hue = ((r - g) / delta) + 4;
        }

        return (hue * 60.0, saturation, lightness);
    }

    public static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        if (saturation <= 0)
        {
            var v = (byte)Math.Round(lightness * 255);
            return Color.FromArgb(255, v, v, v);
        }

        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        var m = lightness - (c / 2);
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private struct BinAccumulator
    {
        public int Count;
        public long SumR;
        public long SumG;
        public long SumB;

        public void Add(byte r, byte g, byte b)
        {
            Count++;
            SumR += r;
            SumG += g;
            SumB += b;
        }

        public readonly Color Average() => Color.FromArgb(
            255,
            (byte)(SumR / Count),
            (byte)(SumG / Count),
            (byte)(SumB / Count));
    }
}
