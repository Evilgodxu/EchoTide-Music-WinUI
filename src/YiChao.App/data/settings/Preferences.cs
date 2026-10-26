namespace YiChao.Data.Settings;

/// <summary>主题模式：跟随系统、浅色、深色。与忆潮的三档一致。</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>界面语言：跟随系统、简体中文、English。</summary>
public enum AppLanguage
{
    System,
    ChineseSimplified,
    English,
}

/// <summary>关闭按钮行为：直接结束进程，或缩到通知区托盘继续播放。</summary>
public enum CloseBehavior
{
    Exit,
    MinimizeToTray,
}
