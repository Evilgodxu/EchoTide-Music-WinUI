using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using YiChao.Data.Model;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 播放列表面板：铺满窗口自下方升起，列出当前播放队列。
///
/// 与忆潮横屏一致：点曲目即切歌并收起面板，点空白处或关闭键收起；
/// 面板本身不改队列，选哪一首由上层决定。
/// </summary>
public sealed partial class PlaylistPanel : UserControl
{
    private const double SlideMs = 260;

    private const double HiddenOffset = 48;

    public PlaylistPanel()
    {
        InitializeComponent();
        ApplyLanguage();
    }

    /// <summary>选中队列中的某一首，参数为下标。</summary>
    public event EventHandler<int>? TrackChosen;

    public bool IsOpen => RootLayer.Visibility == Visibility.Visible;

    public void ApplyLanguage()
    {
        TitleText.Text = Strings.Playlist;
        EmptyText.Text = Strings.PlaylistEmpty;
        ToolTipService.SetToolTip(CloseButton, Strings.ClosePlaylist);
    }

    /// <summary>按当前队列重铺一遍再升起；传入播放进度里的下标用于定位并标记当前曲目。</summary>
    public void Show(IReadOnlyList<MusicTrack> tracks, int currentIndex)
    {
        Rebuild(tracks, currentIndex);

        if (IsOpen)
        {
            return;
        }

        RootLayer.Visibility = Visibility.Visible;
        Fade(true);
    }

    /// <summary>当前曲目变了：只更新行内标记，不重铺整张列表。</summary>
    public void UpdateCurrent(IReadOnlyList<MusicTrack> tracks, int currentIndex)
    {
        if (IsOpen)
        {
            Rebuild(tracks, currentIndex);
        }
    }

    public void Hide()
    {
        if (IsOpen)
        {
            Fade(false);
        }
    }

    private void Rebuild(IReadOnlyList<MusicTrack> tracks, int currentIndex)
    {
        var rows = new List<PlaylistRow>(tracks.Count);
        for (var i = 0; i < tracks.Count; i++)
        {
            rows.Add(new PlaylistRow(tracks[i], i, i == currentIndex));
        }

        TrackList.ItemsSource = rows;
        CountText.Text = Strings.PlaylistCount(rows.Count);

        var empty = rows.Count == 0;
        TrackList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty && currentIndex >= 0 && currentIndex < rows.Count)
        {
            TrackList.ScrollIntoView(rows[currentIndex]);
        }
    }

    /// <summary>进出：整块面板自下浮起，遮罩同时淡入淡出。</summary>
    private void Fade(bool open)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(SlideMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        void Animate(DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        Animate(Panel, "Opacity", Panel.Opacity, open ? 1 : 0);
        Animate(Backdrop, "Opacity", Backdrop.Opacity, open ? 1 : 0);
        Animate(PanelOffset, "Y", PanelOffset.Y, open ? 0 : HiddenOffset);

        storyboard.Completed += (_, _) =>
        {
            Panel.Opacity = open ? 1 : 0;
            Backdrop.Opacity = open ? 1 : 0;
            PanelOffset.Y = open ? 0 : HiddenOffset;

            if (!open)
            {
                RootLayer.Visibility = Visibility.Collapsed;
            }
        };

        storyboard.Begin();
    }

    private void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistRow row)
        {
            TrackChosen?.Invoke(this, row.Index);
        }
    }

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e) => Hide();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
