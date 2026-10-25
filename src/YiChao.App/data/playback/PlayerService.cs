using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using YiChao.Data.Model;

namespace YiChao.Data.Playback;

/// <summary>播放模式：忆潮控制栏展开后的四种模式中，取其中三种可在本地独立成立的。</summary>
public enum PlayMode
{
    /// <summary>列表循环：播到末尾接回第一首。</summary>
    RepeatAll,

    /// <summary>单曲循环。</summary>
    RepeatOne,

    /// <summary>随机：不与当前曲重复。</summary>
    Shuffle,
}

/// <summary>
/// 播放服务：单个 <see cref="MediaPlayer"/> 承担解码输出，曲目切换即换 Source。
///
/// 事件来自媒体线程，统一经 DispatcherQueue 回到 UI 线程再向上抛，
/// 订阅方因此不必自己处理线程切换。位置不另行计时，直接读 PlaybackSession.Position——
/// 那是系统按输出时钟推进的值，跟着它走不会与声音漂移。
/// </summary>
public sealed class PlayerService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Random _random = new();

    private IReadOnlyList<MusicTrack> _playlist = Array.Empty<MusicTrack>();
    private int _currentIndex = -1;
    private bool _disposed;

    public PlayerService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        // 不接管系统媒体控制：传输控件与按键映射不在本轮范围内，关掉可避免额外的会话生命周期问题
        _player.CommandManager.IsEnabled = false;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;

        // 播放状态的真相在会话里：Play() 之后要等解码真正起步才变成 Playing，
        // 只在自己的调用点上报会漏掉这段延迟，图标因此停在播放键上不变
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    /// <summary>当前曲目变更（含自动续播）。参数为新下标，-1 表示无曲目。</summary>
    public event Action<int>? CurrentIndexChanged;

    /// <summary>播放/暂停状态变更。</summary>
    public event Action? PlayingChanged;

    public IReadOnlyList<MusicTrack> Playlist => _playlist;

    public int CurrentIndex => _currentIndex;

    public MusicTrack? Current => _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

    public PlayMode Mode { get; set; } = PlayMode.RepeatAll;

    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public TimeSpan Position => _player.PlaybackSession.Position;

    public TimeSpan Duration
    {
        get
        {
            var natural = _player.PlaybackSession.NaturalDuration;
            return natural > TimeSpan.Zero ? natural : Current?.Duration ?? TimeSpan.Zero;
        }
    }

    public void SetPlaylist(IReadOnlyList<MusicTrack> playlist)
    {
        _playlist = playlist;
        _currentIndex = -1;
    }

    /// <summary>
    /// 播放指定下标。<paramref name="autoPlay"/> 为 false 时只装载不发声（用于恢复上次曲目）。
    /// </summary>
    public async Task PlayAsync(int index, bool autoPlay = true)
    {
        if (index < 0 || index >= _playlist.Count)
        {
            return;
        }

        var track = _playlist[index];
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.FilePath);
            _currentIndex = index;
            _player.Source = MediaSource.CreateFromStorageFile(file);
            if (autoPlay)
            {
                _player.Play();
            }
            else
            {
                _player.Pause();
            }

            CurrentIndexChanged?.Invoke(index);
            PlayingChanged?.Invoke();
        }
        catch (Exception)
        {
            // 文件不可读（被移动或占用）即跳过到下一首，避免卡在同一处
            Next();
        }
    }

    public void TogglePlayPause()
    {
        if (Current is null)
        {
            if (_playlist.Count > 0)
            {
                _ = PlayAsync(0);
            }

            return;
        }

        if (IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }

        PlayingChanged?.Invoke();
    }

    public void Next()
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        if (Mode == PlayMode.Shuffle && _playlist.Count > 1)
        {
            var shuffled = _currentIndex;
            while (shuffled == _currentIndex)
            {
                shuffled = _random.Next(_playlist.Count);
            }

            _ = PlayAsync(shuffled);
            return;
        }

        var next = _currentIndex < 0 ? 0 : _currentIndex + 1;
        if (next >= _playlist.Count)
        {
            next = 0;
        }

        _ = PlayAsync(next);
    }

    public void Previous()
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        // 播放超过 3 秒时，上一曲先回到本曲开头，与常见播放器一致
        if (Position > TimeSpan.FromSeconds(3))
        {
            Seek(TimeSpan.Zero);
            return;
        }

        var previous = _currentIndex <= 0 ? _playlist.Count - 1 : _currentIndex - 1;
        _ = PlayAsync(previous);
    }

    public void Seek(TimeSpan position) => _player.PlaybackSession.Position = position;

    public void SetMode(PlayMode mode) => Mode = mode;

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args) =>
        _dispatcher.TryEnqueue(() => PlayingChanged?.Invoke());

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (Mode == PlayMode.RepeatOne)
            {
                Seek(TimeSpan.Zero);
                sender.Play();
                PlayingChanged?.Invoke();
                return;
            }

            if (Mode == PlayMode.Shuffle && _playlist.Count > 1)
            {
                var next = _currentIndex;
                while (next == _currentIndex)
                {
                    next = _random.Next(_playlist.Count);
                }

                _ = PlayAsync(next);
                return;
            }

            Next();
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _dispatcher.TryEnqueue(Next);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.Dispose();
    }
}
