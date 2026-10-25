using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YiChao.Ui.Component.Window;

/// <summary>
/// 自绘的标题按钮：最小化、最大化/还原、关闭。
///
/// 不用系统那排的原因：系统标题按钮的前景与底色由 DWM 绘制，颜色参数不接受 alpha，
/// 无法随浮层淡出。把系统标题栏整条收掉、自己画三枚等尺寸的按钮，
/// 显隐就与浮层同步，位置与观感仍按 Windows 的 46×32 惯例。
/// </summary>
public sealed partial class CaptionButtons : UserControl
{
    private AppWindow? _appWindow;

    public CaptionButtons()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _appWindow = App.MainWindow?.AppWindow;
        if (_appWindow is null)
        {
            return;
        }

        _appWindow.Changed += OnWindowChanged;
        UpdateMaximizeGlyph();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_appWindow is not null)
        {
            _appWindow.Changed -= OnWindowChanged;
        }
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
        {
            UpdateMaximizeGlyph();
        }
    }

    /// <summary>最大化后换成「还原」字形，与系统按钮的行为一致。</summary>
    private void UpdateMaximizeGlyph()
    {
        var maximized = _appWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        ToolTipService.SetToolTip(MaximizeButton, maximized ? "还原" : "最大化");
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }

        UpdateMaximizeGlyph();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _appWindow?.Destroy();
}
