<div align="center">

<img src="docs/icon.png" width="96" alt="忆潮音乐" />

# 忆潮音乐 · Windows 版

**沉浸聆听，忠于原声**

[English](README.md) | **简体中文**

![License](https://img.shields.io/badge/license-AGPL--3.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-brightgreen)
![Version](https://img.shields.io/badge/version-0.0.1-informational)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![WinUI](https://img.shields.io/badge/WinUI-3-blue)
![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.5.1-blue)
![Arch](https://img.shields.io/badge/arch-x64-orange)

</div>

## 简介

忆潮音乐的 Windows 原生桌面版，用 C# 与 WinUI 3 按[安卓版](https://github.com/Evilgodxu/EchoTide-Music)横屏播放器的形制重做：封面居左，歌词居右，标题栏与控制栏在指针靠近窗口边缘时浮现。它不套壳、不依赖浏览器内核，播放走系统媒体管线。

当前这版只做本地曲库：扫描、播放、歌词、封面、队列与设置都能离线使用，应用本身不联网。安卓版上的在线搜索、代理音源、元数据编辑等能力尚未移植，之后按进度逐项补齐。

## 功能

### 本地曲库

- 目录制扫描：可以添加任意多个音乐目录，也可以从系统「音乐」库起步；只扫描选中的目录。
- 支持 MP3、FLAC、WAV、M4A/M4B/MP4、AAC、WMA、OGG、Opus；时长不足 30 秒的按提示音跳过。
- 元数据直接读文件本身：MP3 的 ID3v2、FLAC/OGG 的 Vorbis 注释、MP4 原子、WAV 块，不依赖只覆盖已索引目录的 Windows Search。
- 与音频同目录、同名的 `.lrc` 自动配对。
- 启动先读上次扫描的快照出画面，后台重扫对账；曲库变了也停在当前这首。
- 扫描进度逐首推进。

### 播放

- 列表循环、单曲循环、随机播放三种模式。
- 支持「下一首播放」插队，多首按插入顺序。
- 播完自动续播；文件读不到时跳过；已播过 3 秒时按上一曲先回到本曲开头。
- 进度条显示当前位置。

### 正在播放

- 封面取自内嵌图（经系统缩略图管线），四边羽化后融进同源背景，背景色由封面取色。
- 音频信息：格式 · 位深/采样率 · 平均比特率。
- 横屏形制：左封面右歌词，标题栏与控制栏贴边浮现。

### 歌词

- 支持普通 LRC、行内逐字标签、`[tr]` 译文块与小时制时间戳；同一行叠写的多个时间戳会拆成多行。
- 内嵌歌词：MP3 的 USLT、FLAC/OGG 的 LYRICS/SYNCEDLYRICS、M4A 的 ©lyr。
- 当前行居中，按字序扫光并放大，带译文行；上下边缘各按半行渐隐，滚动与扫光跟随屏幕刷新率。
- 编码先认 BOM，再严格 UTF-8，最后回落 GB18030。

### 播放列表

- 提交式搜索：回车才搜，标题/歌手/专辑先命中，未命中的再按歌词行（原文与译文）查。
- 排序：默认、收藏、新旧、标题、歌手、专辑、时长，支持正序与逆序。
- 收藏、下一首播放、定位播放、刷新曲库、滚动至顶。

### 桌面集成

- 同一用户只允许一个实例，再次启动会唤醒已有窗口。
- 可选托盘图标；关闭按钮可设为退出或最小化到托盘。
- 沉浸式标题栏：系统标题栏收起，三枚窗口按钮自绘；顶栏与控制栏按指针位置浮现。
- 双击左键播放/暂停，双击右键最大化；F11 全屏；空格与左右键播控；Esc 逐层返回。
- Per-Monitor V2 DPI 感知。

### 设置

- 主题（跟随系统/浅色/深色）与语言（跟随系统/简体中文/English）即时生效。
- 关闭按钮行为、歌词排版（字号、显示行数、行间距）、曲库目录，页脚显示版本号。

## 技术栈

| 层次 | 技术 |
| --- | --- |
| 语言 | C# / .NET 10 |
| UI | WinUI 3（Windows App SDK 2.5.1）+ XAML |
| 播放 | Windows.Media.Playback.MediaPlayer |
| 元数据 | 自写 ID3v2、FLAC、MP4、WAV 标签读取，无第三方解析库 |
| 封面 | 系统媒体缩略图 + 像素级四边羽化 + 取色 |
| 歌词 | 自写 LRC 解析（逐字、译文）与内嵌歌词读取 |
| 持久化 | JSON，位于 `%LOCALAPPDATA%\YiChao` |
| 分发 | 非打包 x64；Windows App SDK 自包含，.NET 运行时框架依赖 |
| 构建 | .NET SDK 10、dotnet CLI |

## 项目结构

```
.
├── docs/
│   └── icon.png                        # 应用图标（取自忆潮音乐）
├── src/
│   └── YiChao.App/
│       ├── data/                       # 数据层
│       │   ├── library/                #   目录扫描与曲库快照
│       │   ├── lyrics/                 #   LRC 解析、内嵌歌词读取与歌词缓存
│       │   ├── metadata/               #   封面取图与羽化、取色、音频规格、标签解析
│       │   │   └── tags/               #     ID3v2 / FLAC / MP4 / WAV 标签读取
│       │   ├── model/                  #   曲目模型
│       │   ├── playback/               #   播放服务、播放模式、插队队列与排序
│       │   └── settings/               #   偏好模型与 settings.json 读写
│       ├── screens/                    # 页面
│       │   ├── home/                   #   首页播放器
│       │   │   ├── component/          #     background / bar / dialog / player / playlist
│       │   │   └── expanded/           #     横屏组装器
│       │   └── settings/               #   设置页（component/entry）
│       ├── ui/                         # 全局共享 UI
│       │   ├── component/              #   封面渐隐、图标、窗口按钮、单实例、托盘
│       │   └── theme/                  #   配色与中英文案
│       ├── App.xaml.cs                 # 应用入口：单实例、托盘、关闭行为、崩溃日志
│       ├── MainWindow.xaml.cs          # 窗口：沉浸式标题栏、快捷键、全屏
│       ├── YiChao.App.csproj
│       └── app.manifest
├── .gitignore
├── LICENSE
└── README.md / README.zh-CN.md
```

## 快速开始

### 环境要求

- Windows 10 1809（build 17763）或更高，仅 x64；建议 Windows 11。
- 构建需要 .NET 10 SDK；运行需要 .NET 10 Desktop Runtime（当前发布为框架依赖）。

### 构建

```powershell
git clone https://github.com/Evilgodxu/EchoTide-Music-WinUI.git
cd EchoTide-Music-WinUI

dotnet build src/YiChao.App/YiChao.App.csproj -c Release
```

可执行文件输出到 `src\YiChao.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\`。

### 发布

```powershell
dotnet publish src/YiChao.App/YiChao.App.csproj -c Release -o publish/0.0.1
```

发布目录自带 Windows App SDK 运行时；若目标机不想预装 .NET 10 Desktop Runtime，发布时加 `-p:SelfContained=true`。

## 数据与日志

- `%LOCALAPPDATA%\YiChao\settings.json`：曲库目录、主题、语言、关闭行为、歌词排版、收藏。
- `%LOCALAPPDATA%\YiChao\library.json`：曲库快照。
- 可执行文件旁的 `crash.log`：未处理异常堆栈。

## 移植进度

Windows 版仍在把安卓版的能力逐项搬过来。这一版只覆盖本地曲库，在线搜索、代理音源、元数据编辑等尚未实现，之后按进度补齐。

## 致谢

- 应用图标沿用忆潮音乐安卓版。

## 许可证

[AGPL-3.0](LICENSE) © 2026 Evilgodxu
