using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using YiChao.Screens.Home.Component.Bar;
using YiChao.Screens.Home.Component.Playlist;
using YiChao.Screens.Home.Component.Player;

namespace YiChao.Screens.Home.Expanded;

/// <summary>指针所处的边缘唤起带。</summary>
public enum ChromeEdge
{
    None,
    Top,
    Bottom,
}

/// <summary>
/// 横屏组装器：把封面栏、歌词栏与上、下两条浮层组装成横屏播放页。
///
/// 形态职责只在本文件：封面取短边 75% 居中、歌词区平铺占满右栏、两条浮层各自独立显隐。
///
/// 显隐由两个来源决定，取并集：指针停在对应边缘带里（悬停），或刚被点击锁定（两秒后解锁）。
/// 悬停的进出即时生效——指针离开那条带就收起，不等计时；
/// 点击唤出的则给两秒停留时间，够看一眼标题或点一下传输键。
/// 两条带分开算：停在上边缘只该唤出标题栏，不该顺手把控制栏也拉出来。
/// 业务状态一概不在这里。
/// </summary>
public sealed partial class ExpandedAssembly : UserControl
{
    /// <summary>封面边长占左栏较短边的比例，居中留出四边羽化的过渡带。</summary>
    private const double CoverFraction = 0.75;

    /// <summary>点击唤出后的停留时长。</summary>
    private const double ChromeLatchMs = 2000;

    private const double ChromeFadeMs = 300;

    /// <summary>收起时的纵向位移：标题栏退到上方、控制栏退到下方。</summary>
    private const double TopBarHiddenOffset = -48;

    private const double ControlsHiddenOffset = 48;

    /// <summary>双击判定的时间窗：两次按下相隔在此之内算一次双击。</summary>
    private const long DoubleClickMs = 400;

    /// <summary>单击切换浮层前的等待：等过双击窗内可能到来的第二次按下，双击才不会顺带闪一下浮层。</summary>
    private const double SingleTapDelayMs = 260;

    private readonly DispatcherQueueTimer _latchTimer;

    /// <summary>等待判定的那次单击：双击到来即取消。</summary>
    private readonly DispatcherQueueTimer _singleTapTimer;

    private bool _topHovered;
    private bool _bottomHovered;
    private bool _topLatched;
    private bool _bottomLatched;

    private long _lastLeftPress;
    private long _lastRightPress;

    /// <summary>本次单击已经把浮层翻过了：双击到来时据此翻回去。</summary>
    private bool _singleTapFired;

    public ExpandedAssembly()
    {
        InitializeComponent();

        _latchTimer = DispatcherQueue.CreateTimer();
        _latchTimer.Interval = TimeSpan.FromMilliseconds(ChromeLatchMs);
        _latchTimer.IsRepeating = false;
        _latchTimer.Tick += (_, _) =>
        {
            _topLatched = false;
            _bottomLatched = false;
            Refresh();
        };

        _singleTapTimer = DispatcherQueue.CreateTimer();
        _singleTapTimer.Interval = TimeSpan.FromMilliseconds(SingleTapDelayMs);
        _singleTapTimer.IsRepeating = false;
        _singleTapTimer.Tick += (_, _) =>
        {
            _singleTapTimer.Stop();
            _singleTapFired = true;
            ToggleChrome();
        };

        // 收起态作为初始位置：首次唤出才有滑入的位移
        TopBarOffset.Y = TopBarHiddenOffset;
        ControlsOffset.Y = ControlsHiddenOffset;

        // 封面不截获点击，整幅区域的点击都落到下方检测层
        Cover.IsHitTestVisible = false;
        Lyrics.IsHitTestVisible = false;
        TapLayer.PointerPressed += OnTapLayerPressed;

        // 模式选项展开期间铺开点击遮罩；展开本身就说明手还在控制栏上，顺手续一次停留
        Controls.ModeSelectorChanged += (_, expanded) =>
        {
            ModeScrim.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            KeepChromeAlive();
        };
    }

    /// <summary>播放页双击左键：切换播放/暂停。</summary>
    public event Action? PlayPauseRequested;

    /// <summary>播放页双击右键：窗口最大化与还原。</summary>
    public event Action? WindowStateToggleRequested;

    public CoverBlend CoverView => Cover;

    public LyricsPanel LyricsView => Lyrics;

    public PlayerControls ControlsView => Controls;

    public PlaylistPanel PlaylistView => Playlist;

    public HomeTopBar TopBarView => TopBar;

    /// <summary>标题栏浮层显隐变更：窗口右上角的标题按钮据此同步。</summary>
    public event Action<bool>? TopChromeVisibilityChanged;

    public bool IsTopChromeVisible { get; private set; }

    public bool IsBottomChromeVisible { get; private set; }

    /// <summary>任一条浮层在场即视为浮层可见。</summary>
    public bool IsChromeVisible => IsTopChromeVisible || IsBottomChromeVisible;

    /// <summary>指针进出边缘带：进入即唤出该边，离开即收起该边。</summary>
    public void SetHoverEdge(ChromeEdge edge)
    {
        _topHovered = edge == ChromeEdge.Top;
        _bottomHovered = edge == ChromeEdge.Bottom;
        Refresh();
    }

    /// <summary>点击空白处的整体切换：两条同时动，任一条在场就一起收起。</summary>
    public void ToggleChrome()
    {
        var show = !IsChromeVisible;
        _topLatched = show;
        _bottomLatched = show;
        _latchTimer.Stop();
        if (show)
        {
            _latchTimer.Start();
        }

        Refresh();
    }

    /// <summary>强制收起两条浮层：进入别的页面时清掉悬停与点击锁定，别把浮层留在上层。</summary>
    public void DismissChrome()
    {
        _topHovered = false;
        _bottomHovered = false;
        _topLatched = false;
        _bottomLatched = false;
        _latchTimer.Stop();
        Controls.CollapseModeSelector();
        Refresh();
    }

    /// <summary>操作浮层上的控件时重新计时，避免操作到一半被收起。</summary>
    public void KeepChromeAlive()
    {
        if (_topLatched || _bottomLatched)
        {
            _latchTimer.Stop();
            _latchTimer.Start();
        }
    }

    /// <summary>音频信息条文本：空串表示规格还没读到，行高照留。</summary>
    public void SetFormatInfo(string text) => FormatInfo.Text = text;

    /// <summary>底边进度：按当前曲目的播放占比填充，无曲目时归零。</summary>
    public void SetProgress(double fraction, bool available) =>
        ProgressScale.ScaleX = available ? Math.Clamp(fraction, 0, 1) : 0;

    /// <summary>
    /// 检测层上的按下：左键单击切换浮层，左键双击切播放，右键双击切窗口最大化。
    ///
    /// 单击不是立刻动作，而是等一个双击窗——否则双击的第一下会先把浮层翻出来再被第二下带走。
    /// 等待期间第二次按下到来即取消并翻回已经发生的动作，画面不残留翻动痕迹。
    /// 触摸与笔没有按键之分，按下即切换浮层。
    /// </summary>
    private void OnTapLayerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TapLayer);
        var properties = point.Properties;
        var now = Environment.TickCount64;

        if (properties.IsRightButtonPressed)
        {
            if (now - _lastRightPress <= DoubleClickMs)
            {
                _lastRightPress = 0;
                WindowStateToggleRequested?.Invoke();
            }
            else
            {
                _lastRightPress = now;
            }

            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            // 触摸与笔没有按键之分，按下即切换浮层
            ToggleChrome();
            return;
        }

        if (now - _lastLeftPress <= DoubleClickMs)
        {
            _lastLeftPress = 0;
            _singleTapTimer.Stop();
            if (_singleTapFired)
            {
                // 上一次单击已经把浮层翻过去了，双击要的是「不翻」，这里翻回去
                ToggleChrome();
                _singleTapFired = false;
            }

            PlayPauseRequested?.Invoke();
            return;
        }

        _lastLeftPress = now;
        _singleTapFired = false;
        _singleTapTimer.Stop();
        _singleTapTimer.Start();
    }

    private void Refresh()
    {
        SetTopVisible(_topHovered || _topLatched);
        SetBottomVisible(_bottomHovered || _bottomLatched);
    }

    private void SetTopVisible(bool visible)
    {
        if (IsTopChromeVisible == visible)
        {
            return;
        }

        IsTopChromeVisible = visible;
        TopBarHost.IsHitTestVisible = visible;
        AnimateHost(TopBarHost, TopBarOffset, TopBarHiddenOffset, visible);
        TopChromeVisibilityChanged?.Invoke(visible);
    }

    private void SetBottomVisible(bool visible)
    {
        if (IsBottomChromeVisible == visible)
        {
            return;
        }

        IsBottomChromeVisible = visible;
        ControlsHost.IsHitTestVisible = visible;
        if (!visible)
        {
            // 控制栏退场时模式选项一起收：遮罩不能留在一条已经消失的栏下面
            Controls.CollapseModeSelector();
        }

        AnimateHost(ControlsHost, ControlsOffset, ControlsHiddenOffset, visible);
        AnimateOpacity(ProgressHost, visible ? 1 : 0);
    }

    /// <summary>点控制栏以外的位置：先收起模式选项，这一次点击不再顺带收起浮层。</summary>
    private void OnModeScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        Controls.CollapseModeSelector();

    /// <summary>浮层进出：淡入淡出的同时沿边缘方向滑一小段，标题栏自上、控制栏自下。</summary>
    /// <param name="hiddenOffset">收起时的纵向位移：标题栏为负、控制栏为正。</param>
    private void AnimateHost(UIElement host, TranslateTransform offset, double hiddenOffset, bool visible)
    {
        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(ChromeFadeMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        void Animate(DependencyObject target, string property, double start, double end)
        {
            var animation = new DoubleAnimation
            {
                From = start,
                To = end,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        Animate(host, "Opacity", host.Opacity, visible ? 1 : 0);
        Animate(offset, "Y", offset.Y, visible ? 0 : hiddenOffset);
        storyboard.Begin();
    }

    /// <summary>只淡入淡出的元素（如底边进度条）：不参与位移，跟着控制栏一起进出。</summary>
    private static void AnimateOpacity(UIElement element, double target)
    {
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(ChromeFadeMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnCoverZoneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var side = Math.Min(e.NewSize.Width, e.NewSize.Height) * CoverFraction;
        Cover.Width = side;
        Cover.Height = side;
    }
}
