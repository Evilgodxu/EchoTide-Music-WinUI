using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using YiChao.Data.Model;
using YiChao.Screens.Home.Component.Dialog;
using YiChao.Screens.Home.Expanded;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home;

/// <summary>
/// 首页骨架：只做装配与事件转接。背景、组装器、空库提示三层在此叠放，
/// 具体形态与组件排布交给组装器，业务判断留在视图模型。
/// </summary>
public sealed partial class HomeScreen : UserControl
{
    /// <summary>顶部唤起带高度：覆盖标题按钮条与标题栏本身。</summary>
    private const double TopHoverHeight = 64;

    /// <summary>底部唤起带高度：控制栏 48 加留白。</summary>
    private const double BottomHoverHeight = 96;

    private const double CaptionFadeMs = 300;

    private readonly HomeViewModel _viewModel = new();

    private bool _settingsOpen;

    /// <summary>播放列表面板铺开时停在的那一首，用于判断要不要重铺行内标记。</summary>
    private int _playlistCurrent = -1;

    public HomeScreen()
    {
        InitializeComponent();
        WireComponents();
        App.Player = _viewModel;
        App.Home = this;

        _viewModel.TrackPresented += OnTrackPresented;
        _viewModel.PlaybackStateChanged += OnPlaybackStateChanged;
        _viewModel.Tick += OnTick;
        _viewModel.UiState.PropertyChanged += (_, _) => ReflectUiState();
        Loaded += OnLoaded;
    }

    private void WireComponents()
    {
        var controls = Assembly.ControlsView;
        controls.PlayPauseRequested += (_, _) =>
        {
            _viewModel.TogglePlayPause();
            Assembly.KeepChromeAlive();
        };
        controls.PreviousRequested += (_, _) =>
        {
            _viewModel.Previous();
            Assembly.KeepChromeAlive();
        };
        controls.NextRequested += (_, _) =>
        {
            _viewModel.Next();
            Assembly.KeepChromeAlive();
        };
        controls.ModeSelected += (_, mode) =>
        {
            _viewModel.SetMode(mode);
            Assembly.KeepChromeAlive();
        };
        controls.PlaylistRequested += (_, _) => OpenPlaylist();

        // 播放页的双击：左键切播放，右键切窗口最大化
        Assembly.PlayPauseRequested += () => _viewModel.TogglePlayPause();
        Assembly.WindowStateToggleRequested += () => (App.MainWindow as MainWindow)?.ToggleMaximize();

        Assembly.PlaylistView.TrackChosen += (_, index) =>
        {
            // 点到正在播放的那首就当作播放/暂停，与忆潮的列表行为一致
            if (index == _viewModel.CurrentIndex)
            {
                _viewModel.TogglePlayPause();
            }
            else
            {
                _viewModel.PlayAt(index);
            }

            Assembly.PlaylistView.Hide();
        };

        // 面板里的行尾动作与工具栏：状态由视图模型改，改完回填给面板
        Assembly.PlaylistView.FavoriteQuery = _viewModel.IsFavorite;
        Assembly.PlaylistView.QueuedQuery = _viewModel.IsQueuedNext;
        Assembly.PlaylistView.FavoriteToggled += (_, track) =>
        {
            var favorite = _viewModel.ToggleFavorite(track);
            Assembly.PlaylistView.SetFavorite(track, favorite);
        };
        Assembly.PlaylistView.PlayNextToggled += (_, track) =>
            Assembly.PlaylistView.SetQueued(track, _viewModel.ToggleQueueNext(track));
        Assembly.PlaylistView.RefreshRequested += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            Assembly.PlaylistView.SetRefreshing(false);
        };

        // 重扫换了队列：面板开着就按新队列重铺一遍
        _viewModel.LibraryRefreshed += () =>
        {
            if (Assembly.PlaylistView.IsOpen)
            {
                OpenPlaylist();
            }
        };

        // 标题按钮与设置入口都是自绘的，平时随标题栏浮层一起淡入淡出
        Assembly.TopChromeVisibilityChanged += visible =>
        {
            SetCaptionVisible(visible);
            SetSettingsEntryVisible(visible);
        };

        Strings.Changed += ApplyLanguage;

        SettingsPage.BackRequested += (_, _) => CloseSettings();
        SettingsPage.ThemeChangeRequested += (_, mode) => _viewModel.SetTheme(mode);
        SettingsPage.LanguageChangeRequested += (_, language) => _viewModel.SetLanguage(language);
        SettingsPage.CloseBehaviorChangeRequested += (_, behavior) => _viewModel.SetCloseBehavior(behavior);
        SettingsPage.LyricLayoutChangeRequested += (_, layout) =>
        {
            _viewModel.SetLyricLayout(layout);
            Assembly.LyricsView.ApplyLayoutSettings();
        };
        SettingsPage.ManageFoldersRequested += async (_, _) => await PromptForFoldersAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        (App.MainWindow as MainWindow)?.UseImmersiveTitleBar(TitleBarDragArea);

        // 启动即收起态，标题按钮不该一开机就露出来
        SetCaptionVisible(Assembly.IsTopChromeVisible);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var hasTracks = await _viewModel.InitializeAsync();
        OnPlaybackStateChanged();

        // 平台音乐库与上次目录都没扫到东西：交给用户手选，而不是留一个空页面
        if (!hasTracks)
        {
            await PromptForFoldersAsync();
        }
    }

    /// <summary>
    /// 指针靠近上下边缘时各自唤起对应那条浮层，离开该带即收起。
    /// 判定按指针在页面内的纵向位置：顶部一巴掌高唤出标题栏，底部一小段唤出控制栏。
    /// </summary>
    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // 设置页在场时它独占整屏，边缘唤起不该在它下层翻出播放器浮层
        if (_settingsOpen || Assembly.PlaylistView.IsOpen)
        {
            return;
        }

        var y = e.GetCurrentPoint(Root).Position.Y;
        if (y <= TopHoverHeight)
        {
            Assembly.SetHoverEdge(ChromeEdge.Top);
        }
        else if (y >= Root.ActualHeight - BottomHoverHeight)
        {
            Assembly.SetHoverEdge(ChromeEdge.Bottom);
        }
        else
        {
            Assembly.SetHoverEdge(ChromeEdge.None);
        }
    }

    /// <summary>指针离开窗口：两条边缘带一并算作离开。</summary>
    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e) =>
        Assembly.SetHoverEdge(ChromeEdge.None);

    private void SetCaptionVisible(bool visible)
    {
        Caption.IsHitTestVisible = visible;
        AnimateOpacity(Caption, visible ? 1 : 0);
    }

    private void SetSettingsEntryVisible(bool visible)
    {
        SettingsButton.IsHitTestVisible = visible;
        AnimateOpacity(SettingsButton, visible ? 1 : 0);
    }

    private static void AnimateOpacity(UIElement element, double target)
    {
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(CaptionFadeMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnTrackPresented()
    {
        Backdrop.ApplyPalette(_viewModel.Palette);
        Assembly.CoverView.SetCover(_viewModel.CoverImage);
        Assembly.LyricsView.SetLines(_viewModel.Lyrics);

        var track = _viewModel.CurrentTrack;
        Assembly.TopBarView.SetTrack(track?.Title, DescribeArtist(track));
        Assembly.SetFormatInfo(_viewModel.FormatInfo);
        OnPlaybackStateChanged();
    }

    private void OnPlaybackStateChanged()
    {
        Assembly.ControlsView.SetPlaying(_viewModel.IsPlaying);
        Assembly.ControlsView.SetMode(_viewModel.Mode);
        Assembly.ControlsView.SetTransportEnabled(_viewModel.HasLibrary);

        // 自动续播与暂停/播放都会走到这里：面板开着就同步行内标记，列表本身不重铺
        if (Assembly.PlaylistView.IsOpen)
        {
            _playlistCurrent = _viewModel.CurrentIndex;
            Assembly.PlaylistView.UpdateState(_playlistCurrent, _viewModel.IsPlaying);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Assembly.LyricsView.UpdatePosition(_viewModel.Position);

        var duration = _viewModel.Duration;
        var fraction = duration > TimeSpan.Zero ? _viewModel.Position.TotalSeconds / duration.TotalSeconds : 0;
        Assembly.SetProgress(fraction, _viewModel.CurrentTrack is not null);
    }

    private void ReflectUiState()
    {
        var state = _viewModel.UiState;
        EmptyLayer.Visibility = state.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = string.IsNullOrEmpty(state.StatusText) ? Strings.EmptyLibrary : state.StatusText;

        // 扫描期间的进度也走这一层，扫完自动隐去
        if (state.IsScanning && EmptyLayer.Visibility == Visibility.Collapsed)
        {
            EmptyLayer.Visibility = Visibility.Visible;
        }
    }

    /// <summary>标题栏艺术家行：专辑名不在这里显示，与忆潮的横屏标题栏一致只给艺术家。</summary>
    private static string DescribeArtist(MusicTrack? track) => track?.Artist ?? string.Empty;

    private async void OnChooseFolderClick(object sender, RoutedEventArgs e) => await PromptForFoldersAsync();

    /// <summary>
    /// 弹曲库目录设置：首次启动扫不到曲目、或用户主动改目录时走这里。
    /// 取消则维持现状——不因为一次取消就把已有目录清空。
    /// </summary>
    private async Task PromptForFoldersAsync()
    {
        var dialog = new LibrarySetupDialog(PickFolderAsync, _viewModel.LibraryPaths)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        EmptyLayer.Visibility = Visibility.Collapsed;
        await _viewModel.ApplyFoldersAsync(dialog.Folders);
        OnPlaybackStateChanged();
        UpdateSettingsSummary();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsOpen)
        {
            return;
        }

        _settingsOpen = true;
        // 进设置页先收起播放器浮层，免得两层叠在一起
        Assembly.DismissChrome();

        // 设置页独占整屏、不再响应边缘唤起，窗口按钮必须常驻，否则最大化与关闭都够不到；
        // 设置入口此时无意义，一并藏起
        SetCaptionVisible(true);
        SetSettingsEntryVisible(false);

        UpdateSettingsSummary();
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void CloseSettings()
    {
        if (!_settingsOpen)
        {
            return;
        }

        _settingsOpen = false;
        SettingsPage.Visibility = Visibility.Collapsed;

        // 窗口按钮交还给浮层逻辑，跟着首页顶栏一起显隐
        SetCaptionVisible(Assembly.IsTopChromeVisible);
        SetSettingsEntryVisible(Assembly.IsTopChromeVisible);
    }

    /// <summary>Esc 退回上一层；返回是否消费了这次按键。</summary>
    public bool HandleBackKey()
    {
        if (_settingsOpen)
        {
            CloseSettings();
            return true;
        }

        if (Assembly.PlaylistView.IsOpen)
        {
            // 有搜索词时 Esc 先清词，再按一次才收起面板
            if (!Assembly.PlaylistView.ClearSearchIfAny())
            {
                Assembly.PlaylistView.Hide();
            }

            return true;
        }

        return false;
    }

    /// <summary>打开播放列表面板：先收起播放器浮层，整屏交给面板。</summary>
    private void OpenPlaylist()
    {
        Assembly.DismissChrome();
        _playlistCurrent = _viewModel.CurrentIndex;
        Assembly.PlaylistView.Show(_viewModel.Tracks, _playlistCurrent, _viewModel.IsPlaying);
    }

    /// <summary>语言切换后重取本页与组件里的文案。</summary>
    private void ApplyLanguage()
    {
        ChooseFolderButton.Content = Strings.ChooseFolder;
        Assembly.LyricsView.ApplyLanguage();
        Assembly.ControlsView.ApplyLanguage();
        Assembly.PlaylistView.ApplyLanguage();
        ReflectUiState();
    }

    private void UpdateSettingsSummary()
    {
        SettingsPage.SetLibrarySummary(_viewModel.LibraryPaths.Count, _viewModel.TrackCount);
        SettingsPage.SetCloseBehavior(_viewModel.CloseBehavior);
    }

    /// <summary>选目录需要窗口句柄，故由视图提供，对话框只负责收集结果。</summary>
    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
