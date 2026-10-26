using System;
using System.Globalization;
using YiChao.Data.Settings;

namespace YiChao.Ui.Theme;

/// <summary>
/// 界面文案：中文与英文两套，跟随系统时按当前区域语言判定。
///
/// 文案集中在这一处而不是散在 XAML 里，是为了切换语言后能整体刷新；
/// 每个用到的控件在语言变更时重新取一次文本（见各视图的 ApplyLanguage）。
/// </summary>
public static class Strings
{
    public static event Action? Changed;

    public static AppLanguage Mode { get; private set; } = AppLanguage.System;

    public static void Initialize(AppLanguage mode) => Mode = mode;

    public static void Set(AppLanguage mode)
    {
        Mode = mode;
        Changed?.Invoke();
    }

    private static bool English => Mode switch
    {
        AppLanguage.ChineseSimplified => false,
        AppLanguage.English => true,
        _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase),
    };

    private static string T(string chinese, string english) => English ? english : chinese;

    // 设置页
    public static string Settings => T("设置", "Settings");

    public static string SectionScan => T("扫描管理", "Library");
    public static string EntryLibraryFolders => T("曲库目录", "Music folders");
    public static string SubtitleManageFolders => T("管理曲库目录", "Manage music folders");
    public static string NoFoldersYet => T("还没有添加目录", "No folders yet");

    public static string SectionAppearance => T("外观", "Appearance");
    public static string EntryTheme => T("主题", "Theme");
    public static string SectionLanguage => T("语言", "Language");
    public static string EntryLanguage => T("语言", "Language");

    public static string SectionWindow => T("窗口", "Window");
    public static string EntryCloseBehavior => T("关闭按钮", "Close button");

    // 歌词排版
    public static string SectionLyricLayout => T("歌词排版", "Lyrics layout");
    public static string EntryLyricFontSize => T("字号", "Font size");
    public static string EntryLyricVisibleLines => T("显示行数", "Visible lines");
    public static string EntryLyricLineSpacing => T("行间距", "Line spacing");
    public static string LyricFontSizeValue(int value) => value.ToString();
    public static string LyricVisibleLinesValue(int value) => T($"{value} 行", $"{value} lines");
    public static string LyricLineSpacingValue(int value) => value.ToString();

    public static string CloseBehaviorName(CloseBehavior behavior) => behavior switch
    {
        CloseBehavior.MinimizeToTray => T("最小化到托盘", "Minimize to tray"),
        _ => T("退出应用", "Exit app"),
    };

    // 托盘
    public static string TrayShowWindow => T("显示主界面", "Show window");
    public static string TrayExit => T("退出", "Exit");
    public static string TrayTooltip => T("忆潮", "YiChao");

    public static string ThemeName(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => T("浅色", "Light"),
        ThemeMode.Dark => T("深色", "Dark"),
        _ => T("跟随系统", "System"),
    };

    public static string LanguageName(AppLanguage language) => language switch
    {
        AppLanguage.ChineseSimplified => "简体中文",
        AppLanguage.English => "English",
        _ => T("跟随系统", "System"),
    };

    public static string FolderSummary(int folders, int tracks) =>
        T($"{folders} 个目录 · {tracks} 首", $"{folders} folders · {tracks} tracks");

    // 首页
    public static string EmptyLibrary => T("曲库为空", "No music yet");
    public static string ChooseFolder => T("选择音乐文件夹", "Choose music folders");
    public static string NoLyrics => T("暂无歌词", "No lyrics");
    public static string UnknownArtist => T("未知艺术家", "Unknown artist");

    // 扫描状态
    public static string StatusNoFolder => T("还没有选择曲库目录", "No library folder selected");
    public static string StatusScanning => T("正在扫描曲库…", "Scanning library…");
    public static string StatusScanningProgress(int done, int total) =>
        T($"正在扫描曲库… {done}/{total}", $"Scanning library… {done}/{total}");
    public static string StatusNoAudioInFolders => T("所选目录里没有找到音频文件", "No audio files found in the selected folders");
    public static string StatusNoAudioInLibrary => T("系统音乐库里没有找到音频文件", "No audio files found in the Music library");

    // 目录设置对话框
    public static string DialogTitle => T("选择音乐文件夹", "Choose music folders");
    public static string DialogBody => T(
        "添加存放音乐的文件夹，只扫描所选目录。文件夹数量不限，可随时增删。",
        "Add folders that contain music. Only the selected folders are scanned; you can add or remove them at any time.");
    public static string DialogAdd => T("添加文件夹…", "Add folder…");
    public static string DialogRemove => T("移除", "Remove");
    public static string DialogEmpty => T("还没有添加文件夹", "No folders added yet");
    public static string DialogConfirm => T("开始扫描", "Start scanning");
    public static string Confirm => T("确定", "OK");
    public static string DialogCancel => T("取消", "Cancel");

    // 控制栏
    public static string PlayMode => T("播放模式", "Play mode");
    public static string Previous => T("上一曲", "Previous");
    public static string Play => T("播放", "Play");
    public static string Pause => T("暂停", "Pause");
    public static string Next => T("下一曲", "Next");
    public static string Playlist => T("播放列表", "Queue");

    // 播放模式：展开后的三种选项
    public static string PlayModeRepeatAll => T("列表循环", "Repeat all");
    public static string PlayModeRepeatOne => T("单曲循环", "Repeat one");
    public static string PlayModeShuffle => T("随机播放", "Shuffle");

    // 播放列表面板
    public static string PlaylistCount(int tracks) => T($"{tracks} 首", $"{tracks} tracks");
    public static string PlaylistEmpty => T("列表为空", "Nothing queued");
    public static string PlaylistNoMatch => T("没有匹配的曲目", "No matches");
    public static string PlaylistSearchPlaceholder => T("搜索播放列表", "Search queue");
    public static string ClosePlaylist => T("关闭播放列表", "Close queue");

    // 播放列表工具栏
    public static string RefreshLibrary => T("刷新", "Refresh");
    public static string SortFieldTitle => T("排序", "Sort");
    public static string SortDescending => T("逆序", "Descending");
    public static string SortAscending => T("正序", "Ascending");
    public static string SortDefault => T("默认", "Default");
    public static string SortFavorite => T("收藏", "Favorite");
    public static string SortModified => T("新旧", "Modified");
    public static string SortTitle => T("标题", "Title");
    public static string SortArtist => T("歌手", "Artist");
    public static string SortAlbum => T("专辑", "Album");
    public static string SortDuration => T("时长", "Duration");
    public static string ScrollToTop => T("滚动至顶", "Scroll to top");
    public static string LocatePlaying => T("定位播放", "Locate playing");
    public static string PlayNext => T("下一首播放", "Play next");
    public static string CancelPlayNext => T("取消下一首播放", "Cancel play next");
}
