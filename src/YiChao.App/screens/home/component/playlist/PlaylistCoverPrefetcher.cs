using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YiChao.Data.Metadata;
using YiChao.Data.Model;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 列表封面邻域预取：把可视区前后各 <see cref="Radius"/> 项的封面提前解码进内存缓存，
/// 滚到该项时首帧就能同步命中，不再先闪一次占位。
///
/// 结构照忆潮拆成两半：「窗口发布」只更新一个最新值，取图循环常驻不重启。
/// 滚动中窗口频繁变化，若每变一次就整批重来，每轮都只开个头就被打断，吞吐趋近于零——
/// 所以窗口变化只影响「下一批取谁」，已发出的那一批照常跑完（结果已进缓存，不白费）。
///
/// 取图顺序按离可视区由近及远；同距时优先取将要进入视口的一侧，按滚动方向决定。
/// </summary>
internal sealed class PlaylistCoverPrefetcher
{
    /// <summary>预取窗口半径：可视区前后各这么多项。</summary>
    private const int Radius = 120;

    /// <summary>同时在取的张数：缩略图查询与解码都落在系统一侧，少量并发即可打满。</summary>
    private const int Concurrency = 4;

    /// <summary>空转轮询周期：窗口没动、能取的也都取了时，按此周期重扫一次。</summary>
    private const int IdlePollMs = 500;

    private IReadOnlyList<MusicTrack> _tracks = Array.Empty<MusicTrack>();

    private int _sizePx;

    private int _first = -1;

    private int _last = -1;

    private bool _forward = true;

    private volatile bool _running;

    public void Start(IReadOnlyList<MusicTrack> tracks, int sizePx)
    {
        _tracks = tracks;
        _sizePx = sizePx;
        if (_running)
        {
            return;
        }

        _running = true;
        _ = LoopAsync();
    }

    public void Stop()
    {
        _running = false;
        _tracks = Array.Empty<MusicTrack>();
        _first = -1;
        _last = -1;
    }

    /// <summary>发布当前可视区；由滚动与列表重建调用，不阻塞。</summary>
    public void UpdateWindow(int first, int last, bool forward)
    {
        _first = first;
        _last = last;
        _forward = forward;
    }

    private async Task LoopAsync()
    {
        while (_running)
        {
            var tracks = _tracks;
            var first = _first;
            var last = _last;
            var forward = _forward;
            var sizePx = _sizePx;

            if (tracks.Count == 0 || first < 0 || last < first)
            {
                // 列表尚未布局：等窗口出现再开始
                await Task.Delay(IdlePollMs);
                continue;
            }

            var order = BuildOrder(first, last, forward, tracks.Count);
            var unchanged = true;
            for (var start = 0; start < order.Count; start += Concurrency)
            {
                if (!_running || tracks != _tracks || first != _first || last != _last)
                {
                    // 窗口移走：不再开新批，在飞的那一批跑完
                    unchanged = false;
                    break;
                }

                var end = Math.Min(start + Concurrency, order.Count);
                var batch = new Task[end - start];
                for (var i = start; i < end; i++)
                {
                    batch[i - start] = PrefetchAsync(tracks[order[i]], sizePx);
                }

                await Task.WhenAll(batch);
            }

            if (unchanged && _running)
            {
                await Task.Delay(IdlePollMs);
            }
        }
    }

    private static async Task PrefetchAsync(MusicTrack track, int sizePx)
    {
        try
        {
            await CoverThumbnailCache.LoadAsync(track.FilePath, sizePx);
        }
        catch (Exception)
        {
            // 单张取不到不打断整批：滚入视口时行会按需重试
        }
    }

    /// <summary>
    /// 取图顺序：可视区内的先取（眼下正显示占位），再按距离向两侧扩，
    /// 同距时先取滚动前方那一侧。
    /// </summary>
    private static List<int> BuildOrder(int first, int last, bool forward, int total)
    {
        var order = new List<int>(Math.Min(total, (Radius * 2) + 1));
        for (var index = Math.Max(0, first); index <= last && index < total; index++)
        {
            order.Add(index);
        }

        for (var offset = 1; offset <= Radius; offset++)
        {
            var below = last + offset;
            var above = first - offset;
            var leading = forward ? below : above;
            var trailing = forward ? above : below;

            if (leading >= 0 && leading < total)
            {
                order.Add(leading);
            }

            if (trailing >= 0 && trailing < total)
            {
                order.Add(trailing);
            }
        }

        return order;
    }
}
