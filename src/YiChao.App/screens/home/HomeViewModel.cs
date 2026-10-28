using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using YiChao.Data.Library;
using YiChao.Data.Lyrics;
using YiChao.Data.Metadata;
using YiChao.Data.Model;
using YiChao.Data.Playback;
using YiChao.Data.Settings;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home;

/// <summary>
/// 首页状态中枢：持有曲库、播放服务与当前曲目的展示素材（封面、背景取色、歌词）。
///
/// 视图只读这里的字段并订阅事件，不直接接触播放器与文件系统。
/// 展示素材按曲目异步装配，装配完成才通知视图——取图期间视图仍显示上一首的画面，不会先清空再填。
/// </summary>
public sealed class HomeViewModel
{
    private readonly AppSettings _settings;
    private readonly PlayerService _player;
    private readonly DispatcherQueueTimer _ticker;
    private int _presentingIndex = -1;
    private string? _presentedPath;

    public HomeViewModel()
    {
        _settings = App.Settings;
        _player = new PlayerService(DispatcherQueue.GetForCurrentThread());
        _player.CurrentIndexChanged += OnCurrentIndexChanged;
        // 播放/暂停由播放器自己上报：真正起播、播完、解码失败都发生在它那边，
        // 界面不该只在自己发起的那一次调用后猜状态
        _player.PlayingChanged += () => PlaybackStateChanged?.Invoke();

        _ticker = DispatcherQueue.GetForCurrentThread().CreateTimer();
        // 只用来察觉换行与刷新进度条：帧间平滑由歌词面板自己的显示帧时钟负责，
        // 这里取一帧的粒度，换行最多晚一帧被看到，高刷屏上不会觉得歌词慢了半拍
        _ticker.Interval = TimeSpan.FromMilliseconds(16);
        _ticker.IsRepeating = true;
        _ticker.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>曲目展示素材（标题、封面、背景取色、歌词）已就绪。</summary>
    public event Action? TrackPresented;

    /// <summary>播放/暂停或模式变化，视图据此换图标。</summary>
    public event Action? PlaybackStateChanged;

    /// <summary>播放位置推进，约一帧一次，供歌词换行与进度条使用。</summary>
    public event EventHandler? Tick;

    /// <summary>曲库被重扫并换了队列：面板据此重铺列表。</summary>
    public event Action? LibraryRefreshed;

    public HomeUiState UiState { get; } = new();

    public MusicTrack? CurrentTrack => _player.Current;

    public ImageSource? CoverImage { get; private set; }

    public CoverPalette? Palette { get; private set; }

    public IReadOnlyList<LyricLine> Lyrics { get; private set; } = Array.Empty<LyricLine>();

    /// <summary>音频信息条文本：格式 · 位深/采样率 · 比特率，读不到的部分留空。</summary>
    public string FormatInfo { get; private set; } = string.Empty;

    public bool IsPlaying => _player.IsPlaying;

    public PlayMode Mode => _player.Mode;

    /// <summary>当前播放队列，播放列表面板按它铺行。</summary>
    public IReadOnlyList<MusicTrack> Tracks => _player.Playlist;

    public int CurrentIndex => _player.CurrentIndex;

    public TimeSpan Position => _player.Position;

    public ThemeMode Theme => _settings.Theme;

    public AppLanguage Language => _settings.Language;

    public CloseBehavior CloseBehavior => _settings.CloseBehavior;

    public LyricLayoutSettings LyricLayout => _settings.LyricLayout;

    /// <summary>保存歌词排版参数：归一化后写盘，歌词面板由视图重新量一次。</summary>
    public void SetLyricLayout(LyricLayoutSettings layout)
    {
        layout.Normalize();
        _settings.LyricLayout = layout;
        _settings.Save();
    }

    /// <summary>切换关闭行为：写盘后立即同步托盘图标的显隐。</summary>
    public void SetCloseBehavior(CloseBehavior behavior)
    {
        _settings.CloseBehavior = behavior;
        _settings.Save();
        App.ApplyCloseBehavior(behavior);
    }

    /// <summary>切换主题：写盘后立即下发，设置页与窗口按钮同步换色。</summary>
    public void SetTheme(ThemeMode mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        AppTheme.Set(mode);
    }

    /// <summary>切换语言：写盘后立即下发，各视图重取文案。</summary>
    public void SetLanguage(AppLanguage language)
    {
        _settings.Language = language;
        _settings.Save();
        Strings.Set(language);
    }

    public bool HasLibrary => _player.Playlist.Count > 0;

    public int TrackCount => _player.Playlist.Count;

    /// <summary>当前生效的曲库目录。</summary>
    public IReadOnlyList<string> LibraryPaths => _settings.LibraryPaths;

    /// <summary>
    /// 启动装载：有曲库快照就先拿它出画面，重扫放到后台；没有快照才等一次完整扫描。
    ///
    /// 返回是否拿到了曲目。拿不到就把决定权交回界面——由它弹目录设置对话框，
    /// 而不是默默拿一个空目录当结果。平台音乐库只在真的扫到曲目时才登记进配置，
    /// 否则会把一个空目录写死，下次启动看到的仍是同一手空目录。
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        if (_settings.LibraryPaths.Count > 0)
        {
            var cached = LibraryCache.TryLoad(_settings.LibraryPaths);
            if (cached is { Count: > 0 })
            {
                LoadPlaylist(cached, autoPlay: false);
                _ = RefreshFromDiskAsync(reportEmpty: false);
                return true;
            }

            return await ScanAsync(_settings.LibraryPaths, Strings.StatusNoAudioInFolders);
        }

        var platformFolders = await LocalLibraryScanner.GetPlatformMusicFoldersAsync();
        if (platformFolders.Count == 0)
        {
            ShowEmpty(Strings.StatusNoFolder);
            return false;
        }

        var found = await ScanAsync(platformFolders, Strings.StatusNoAudioInLibrary);
        if (found)
        {
            _settings.LibraryPaths.AddRange(platformFolders);
            _settings.Save();
        }

        return found;
    }

    /// <summary>替换曲库目录并重新扫描（目录设置对话框确认后调用）。</summary>
    public async Task<bool> ApplyFoldersAsync(IReadOnlyList<string> folders)
    {
        _settings.LibraryPaths = folders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Save();
        return await ScanAsync(_settings.LibraryPaths, Strings.StatusNoAudioInFolders);
    }

    /// <summary>
    /// 后台对账：用快照出画面之后再真正扫一遍，曲库没变只刷新快照，
    /// 变了就换上新清单并停在当前这首上——用户不该因为一次后台刷新被切歌。
    /// </summary>
    private async Task RefreshFromDiskAsync(bool reportEmpty)
    {
        var folders = _settings.LibraryPaths.ToList();
        var tracks = await LocalLibraryScanner.ScanAsync(folders);

        // 盘符掉线、目录暂时读不到时扫出空清单：此时不推翻已有曲库，
        // 只有用户主动重扫才把「没有曲目」当作结论报出来
        if (tracks.Count == 0)
        {
            if (reportEmpty)
            {
                ShowEmpty(Strings.StatusNoAudioInFolders);
            }

            return;
        }

        LibraryCache.Save(folders, tracks);
        if (SameAs(_player.Playlist, tracks))
        {
            return;
        }

        var currentPath = _player.Current?.FilePath;
        var wasPlaying = _player.IsPlaying;
        _player.SetPlaylist(tracks);

        var index = currentPath is null
            ? -1
            : tracks.FindIndex(track => string.Equals(track.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        _ = index >= 0
            ? _player.PlayAsync(index, wasPlaying)
            : _player.PlayAsync(0, autoPlay: false);
        PlaybackStateChanged?.Invoke();
    }

    private static bool SameAs(IReadOnlyList<MusicTrack> previous, IReadOnlyList<MusicTrack> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < previous.Count; i++)
        {
            if (!string.Equals(previous[i].FilePath, current[i].FilePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previous[i].Title, current[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>扫描给定目录并装载结果。返回是否扫到了曲目，没扫到时的提示由调用方定。</summary>
    private async Task<bool> ScanAsync(IReadOnlyList<string> roots, string emptyHint)
    {
        UiState.IsScanning = true;
        UiState.IsEmpty = false;
        UiState.StatusText = Strings.StatusScanning;

        var progress = new Progress<ScanProgress>(p =>
            UiState.StatusText = Strings.StatusScanningProgress(p.Done, p.Total));

        var tracks = await LocalLibraryScanner.ScanAsync(roots, progress);
        if (tracks.Count == 0)
        {
            ShowEmpty(emptyHint);
            return false;
        }

        LoadPlaylist(tracks, autoPlay: false);
        LibraryCache.Save(roots, tracks);
        return true;
    }

    private void LoadPlaylist(IReadOnlyList<MusicTrack> tracks, bool autoPlay)
    {
        _player.SetPlaylist(tracks);
        UiState.IsScanning = false;
        UiState.IsEmpty = false;
        UiState.StatusText = string.Empty;

        // 装载首曲但不自动发声：启动即播放不是这里的默认行为
        _ = _player.PlayAsync(0, autoPlay);
        PlaybackStateChanged?.Invoke();
        _ticker.Start();
    }

    private void ShowEmpty(string hint)
    {
        UiState.IsScanning = false;
        UiState.IsEmpty = true;
        UiState.StatusText = hint;
    }

    public void TogglePlayPause()
    {
        _player.TogglePlayPause();
        PlaybackStateChanged?.Invoke();
    }

    public void Next()
    {
        _player.Next();
        PlaybackStateChanged?.Invoke();
    }

    public void Previous()
    {
        _player.Previous();
        PlaybackStateChanged?.Invoke();
    }

    /// <summary>指定播放模式：从控制栏展开的选项直接选中，不再逐个轮换。</summary>
    public void SetMode(PlayMode mode)
    {
        _player.SetMode(mode);
        PlaybackStateChanged?.Invoke();
    }

    /// <summary>跳到队列中的某一首（播放列表面板点击）。</summary>
    public void PlayAt(int index)
    {
        _ = _player.PlayAsync(index);
        PlaybackStateChanged?.Invoke();
    }

    public void Seek(TimeSpan position) => _player.Seek(position);

    public TimeSpan Duration => _player.Duration;

    public bool IsFavorite(MusicTrack track) =>
        _settings.Favorites.Contains(track.FilePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>切换某一首的收藏并写盘，返回切换后的状态。</summary>
    public bool ToggleFavorite(MusicTrack track)
    {
        var favorited = IsFavorite(track);
        if (favorited)
        {
            _settings.Favorites.RemoveAll(path =>
                string.Equals(path, track.FilePath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _settings.Favorites.Add(track.FilePath);
        }

        _settings.Save();
        return !favorited;
    }

    public bool IsQueuedNext(MusicTrack track) => _player.IsQueuedNext(track.FilePath);

    /// <summary>插队/取消插队，返回插队后的状态。</summary>
    public bool ToggleQueueNext(MusicTrack track)
    {
        _player.ToggleQueueNext(track);
        return _player.IsQueuedNext(track.FilePath);
    }

    /// <summary>
    /// 重扫曲库并重建队列：当前曲目按路径重定位，播放状态沿用。
    /// 重扫期间面板上的刷新键由界面自行置灰，这里只负责扫描与换队列。
    /// </summary>
    public async Task RefreshAsync()
    {
        var folders = _settings.LibraryPaths.ToList();
        if (folders.Count == 0)
        {
            return;
        }

        var tracks = await LocalLibraryScanner.ScanAsync(folders);
        if (tracks.Count == 0)
        {
            // 盘符掉线、目录暂时读不到：不推翻已有曲库，也不留下空列表
            return;
        }

        LibraryCache.Save(folders, tracks);
        if (SameAs(_player.Playlist, tracks))
        {
            PlaybackStateChanged?.Invoke();
            return;
        }

        var currentPath = _player.Current?.FilePath;
        var wasPlaying = _player.IsPlaying;
        _player.SetPlaylist(tracks);

        var index = currentPath is null
            ? -1
            : tracks.FindIndex(track => string.Equals(track.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        _ = index >= 0
            ? _player.PlayAsync(index, wasPlaying)
            : _player.PlayAsync(0, autoPlay: false);

        PlaybackStateChanged?.Invoke();
        LibraryRefreshed?.Invoke();
    }

    private void OnCurrentIndexChanged(int index) => _ = PresentAsync(index);

    /// <summary>
    /// 装配并发布当前曲目的展示素材。取图与解析歌词失败都按「没有」处理，
    /// 界面退到同源背景与「暂无歌词」，不阻断播放。
    /// </summary>
    private async Task PresentAsync(int index)
    {
        if (index < 0)
        {
            return;
        }

        var track = _player.Current;
        if (track is null)
        {
            return;
        }

        // 后台对账会以同一首曲目重装清单：素材已经就位就不必再取一遍图，
        // 否则每次刷新都会闪一次封面
        if (string.Equals(track.FilePath, _presentedPath, StringComparison.OrdinalIgnoreCase)
            && CoverImage is not null)
        {
            _presentingIndex = index;
            return;
        }

        _presentingIndex = index;
        var lyrics = await Task.Run(() => TrackLyrics.Load(track));
        var spec = await Task.Run(() => AudioSpecReader.Read(track.FilePath, track.Duration));
        var cover = await CoverLoader.LoadAsync(track.FilePath);

        // 取图期间已切到别的曲目：本次结果作废，避免上一首的封面盖住当前曲目
        if (_presentingIndex != index)
        {
            return;
        }

        CoverImage = cover?.Image;
        Palette = cover?.Palette;
        Lyrics = lyrics;
        FormatInfo = spec.Label;
        _presentedPath = track.FilePath;
        TrackPresented?.Invoke();
    }
}
