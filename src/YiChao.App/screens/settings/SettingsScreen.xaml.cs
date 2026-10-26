using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using YiChao.Data.Settings;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Settings;

/// <summary>
/// 设置页骨架：不分标题栏，内容直接铺满窗口——桌面窗口宽度就是可读宽度，
/// 忆潮在平板上限宽居中是因为屏幕窄，照搬只会白白缩掉两侧。
/// 返回走右键与 Esc，页面上不放返回按钮；具体设置项由调用方在 XAML 里组装。
///
/// 主题与语言在本页即时生效：换主题只重刷本页配色与窗口按钮，
/// 换语言重取全部文案，两者都不重建页面。
/// </summary>
public sealed partial class SettingsScreen : UserControl
{
    private int _folderCount;
    private int _trackCount;

    public SettingsScreen()
    {
        InitializeComponent();
        AppTheme.Changed += ApplyTheme;
        Strings.Changed += ApplyLanguage;
        ApplyTheme();
        ApplyLanguage();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? ManageFoldersRequested;

    public event EventHandler<ThemeMode>? ThemeChangeRequested;

    public event EventHandler<AppLanguage>? LanguageChangeRequested;

    public event EventHandler<CloseBehavior>? CloseBehaviorChangeRequested;

    /// <summary>刷新目录条目副标题：目录数与曲目数直接反映当前曲库状态。</summary>
    public void SetLibrarySummary(int folderCount, int trackCount)
    {
        _folderCount = folderCount;
        _trackCount = trackCount;
        ApplySummary();
    }

    private void ApplySummary() =>
        FolderEntry.Subtitle = _folderCount == 0
            ? Strings.NoFoldersYet
            : Strings.FolderSummary(_folderCount, _trackCount);

    private void ApplyLanguage()
    {
        AppearanceLabel.Text = Strings.SectionAppearance;
        LanguageLabel.Text = Strings.SectionLanguage;
        WindowLabel.Text = Strings.SectionWindow;
        ScanLabel.Text = Strings.SectionScan;

        ThemeEntry.Title = Strings.EntryTheme;
        ThemeEntry.Subtitle = Strings.ThemeName(AppTheme.Mode);
        LanguageEntry.Title = Strings.EntryLanguage;
        LanguageEntry.Subtitle = Strings.LanguageName(Strings.Mode);
        CloseBehaviorEntry.Title = Strings.EntryCloseBehavior;
        CloseBehaviorEntry.Subtitle = Strings.CloseBehaviorName(App.Settings.CloseBehavior);

        FolderEntry.Title = Strings.EntryLibraryFolders;
        ApplySummary();
    }

    /// <summary>关闭行为变更后刷新副标题：由调用方在写盘完成后回填。</summary>
    public void SetCloseBehavior(CloseBehavior behavior) =>
        CloseBehaviorEntry.Subtitle = Strings.CloseBehaviorName(behavior);

    private void ApplyTheme()
    {
        RootGrid.Background = new SolidColorBrush(AppTheme.Background);

        foreach (var label in new[] { AppearanceLabel, LanguageLabel, WindowLabel, ScanLabel })
        {
            label.Foreground = new SolidColorBrush(AppTheme.OnSurfaceVariant);
        }

        foreach (var card in new[] { AppearanceCard, LanguageCard, WindowCard, ScanCard })
        {
            card.Background = new SolidColorBrush(AppTheme.Surface);
        }

        foreach (var icon in new[] { ThemeIcon, LanguageIcon, CloseBehaviorIcon, FolderIcon })
        {
            icon.Fill = new SolidColorBrush(AppTheme.OnSurfaceVariant);
        }

        foreach (var entry in new[] { ThemeEntry, LanguageEntry, CloseBehaviorEntry, FolderEntry })
        {
            entry.ApplyTheme();
        }
    }

    private async void OnThemeClick(object sender, EventArgs e)
    {
        var options = new[]
        {
            Strings.ThemeName(ThemeMode.System),
            Strings.ThemeName(ThemeMode.Light),
            Strings.ThemeName(ThemeMode.Dark),
        };
        var index = await PickOptionAsync(Strings.EntryTheme, options, (int)AppTheme.Mode);
        if (index >= 0)
        {
            ThemeChangeRequested?.Invoke(this, (ThemeMode)index);
        }
    }

    private async void OnLanguageClick(object sender, EventArgs e)
    {
        var options = new[]
        {
            Strings.LanguageName(AppLanguage.System),
            Strings.LanguageName(AppLanguage.ChineseSimplified),
            Strings.LanguageName(AppLanguage.English),
        };
        var index = await PickOptionAsync(Strings.EntryLanguage, options, (int)Strings.Mode);
        if (index >= 0)
        {
            LanguageChangeRequested?.Invoke(this, (AppLanguage)index);
        }
    }

    private async void OnCloseBehaviorClick(object sender, EventArgs e)
    {
        var options = new[]
        {
            Strings.CloseBehaviorName(CloseBehavior.Exit),
            Strings.CloseBehaviorName(CloseBehavior.MinimizeToTray),
        };
        var selected = (int)App.Settings.CloseBehavior;
        var index = await PickOptionAsync(Strings.EntryCloseBehavior, options, selected);
        if (index < 0)
        {
            return;
        }

        var behavior = (CloseBehavior)index;
        CloseBehaviorChangeRequested?.Invoke(this, behavior);
        SetCloseBehavior(behavior);
    }

    /// <summary>单选项对话框：确认后返回下标，取消返回 -1。</summary>
    private async Task<int> PickOptionAsync(string title, IReadOnlyList<string> options, int selectedIndex)
    {
        var radio = new RadioButtons
        {
            ItemsSource = options,
            SelectedIndex = Math.Clamp(selectedIndex, 0, options.Count - 1),
            MaxColumns = 1,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = radio,
            PrimaryButtonText = Strings.Confirm,
            CloseButtonText = Strings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? radio.SelectedIndex : -1;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnManageFoldersClick(object sender, EventArgs e) =>
        ManageFoldersRequested?.Invoke(this, EventArgs.Empty);

}
