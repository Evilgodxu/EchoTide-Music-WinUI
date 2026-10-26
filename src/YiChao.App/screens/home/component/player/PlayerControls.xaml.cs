using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using YiChao.Data.Playback;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Player;

/// <summary>
/// 底部控制栏：播放模式 → 上一曲 → 播放/暂停 → 下一曲 → 播放列表。
///
/// 只上报点击，不持有播放状态——模式切换与曲目跳转由上层决定，这里仅按传入状态换图标与可用性。
/// 唯一由自己保管的是模式选项的展开态：它不描述播放，只描述这一排按钮此刻摆什么，
/// 展开与否还要交给组装器铺一层点击遮罩（见 <see cref="ModeSelectorChanged"/>）。
/// </summary>
public sealed partial class PlayerControls : UserControl
{
    /// <summary>模式图标翻转的半圈时长，与忆潮同值。</summary>
    private const double ModeToggleMs = 280;

    /// <summary>已选项的着色；未选项压到 55% 白，补偿收起态由模式键承担的当前模式示意。</summary>
    private static readonly Brush SelectedTint = new SolidColorBrush(Microsoft.UI.Colors.White);

    private static readonly Brush OptionTint = new SolidColorBrush(Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

    private bool _playing;

    public PlayerControls()
    {
        InitializeComponent();
        ApplyLanguage();
        SetModeSelectorExpanded(false, animate: false);
        SetMode(PlayMode.RepeatAll);
    }

    /// <summary>模式选项展开态变更：组装器据此铺开或收起全屏点击遮罩。</summary>
    public event EventHandler<bool>? ModeSelectorChanged;

    /// <summary>选定某个播放模式：只上报，切换与落盘由视图模型做。</summary>
    public event EventHandler<PlayMode>? ModeSelected;

    public event EventHandler? PlayPauseRequested;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    public event EventHandler? PlaylistRequested;

    public bool IsModeSelectorExpanded => ModeOptions.Visibility == Visibility.Visible;

    /// <summary>语言切换后重取各按钮的提示文案与当前播放状态文案。</summary>
    public void ApplyLanguage()
    {
        ToolTipService.SetToolTip(ModeButton, Strings.PlayMode);
        ToolTipService.SetToolTip(PreviousButton, Strings.Previous);
        ToolTipService.SetToolTip(NextButton, Strings.Next);
        ToolTipService.SetToolTip(PlaylistButton, Strings.Playlist);
        ToolTipService.SetToolTip(RepeatAllOption, Strings.PlayModeRepeatAll);
        ToolTipService.SetToolTip(RepeatOneOption, Strings.PlayModeRepeatOne);
        ToolTipService.SetToolTip(ShuffleOption, Strings.PlayModeShuffle);
        SetPlaying(_playing);
    }

    public void SetPlaying(bool playing)
    {
        _playing = playing;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PlayPauseButton, playing ? Strings.Pause : Strings.Play);
    }

    public void SetMode(PlayMode mode)
    {
        ModeRepeatIcon.Visibility = mode == PlayMode.RepeatAll ? Visibility.Visible : Visibility.Collapsed;
        ModeRepeatOneIcon.Visibility = mode == PlayMode.RepeatOne ? Visibility.Visible : Visibility.Collapsed;
        ModeShuffleIcon.Visibility = mode == PlayMode.Shuffle ? Visibility.Visible : Visibility.Collapsed;

        RepeatAllOptionIcon.IconFill = mode == PlayMode.RepeatAll ? SelectedTint : OptionTint;
        RepeatOneOptionIcon.IconFill = mode == PlayMode.RepeatOne ? SelectedTint : OptionTint;
        ShuffleOptionIcon.IconFill = mode == PlayMode.Shuffle ? SelectedTint : OptionTint;
    }

    /// <summary>曲库为空时这几个键置灰：降到 30% 白，与忆潮的禁用态一致。</summary>
    public void SetTransportEnabled(bool enabled)
    {
        var alpha = enabled ? 1.0 : 0.3;
        foreach (var button in new[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.IsEnabled = enabled;
            button.Opacity = alpha;
        }
    }

    public void ToggleModeSelector() => SetModeSelectorExpanded(!IsModeSelectorExpanded);

    public void CollapseModeSelector() => SetModeSelectorExpanded(false);

    private void SetModeSelectorExpanded(bool expanded, bool animate = true)
    {
        if (animate && IsModeSelectorExpanded == expanded)
        {
            return;
        }

        ModeOptions.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        TransportGroup.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;

        if (animate)
        {
            AnimateModeToggle(expanded);
        }
        else
        {
            ApplyModeToggleState(expanded);
        }

        ModeSelectorChanged?.Invoke(this, expanded);
    }

    /// <summary>模式图标整半圈翻转，期间模式图标与右向箭头交叉淡入淡出。</summary>
    private void AnimateModeToggle(bool expanded)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(ModeToggleMs));
        var storyboard = new Storyboard();

        void Animate(DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        Animate(ModeRotation, "Angle", ModeRotation.Angle, expanded ? 180 : 0);
        Animate(ModeGlyph, "Opacity", ModeGlyph.Opacity, expanded ? 0 : 1);
        Animate(ModeArrowIcon, "Opacity", ModeArrowIcon.Opacity, expanded ? 1 : 0);

        // 动画停在终值上，但基础值仍是旧的那份：补写回去，下次翻转的起点才取得到
        storyboard.Completed += (_, _) => ApplyModeToggleState(expanded);
        storyboard.Begin();
    }

    private void ApplyModeToggleState(bool expanded)
    {
        ModeRotation.Angle = expanded ? 180 : 0;
        ModeGlyph.Opacity = expanded ? 0 : 1;
        ModeArrowIcon.Opacity = expanded ? 1 : 0;
    }

    private void OnModeClick(object sender, RoutedEventArgs e) => ToggleModeSelector();

    private void OnModeOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<PlayMode>(tag, out var mode))
        {
            return;
        }

        SetModeSelectorExpanded(false);
        ModeSelected?.Invoke(this, mode);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);

    private void OnPlaylistClick(object sender, RoutedEventArgs e) => PlaylistRequested?.Invoke(this, EventArgs.Empty);
}
