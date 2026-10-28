; 忆潮 · 安装器定义
;
; 载荷是自包含发布目录：.NET 运行时与 Windows App SDK 随包交付，
; 安装完成即可离线运行，不检测也不下载任何运行时。
; 由 installer\build.ps1 编译，版本号与发布目录从命令行传入。
;
; 界面语言只有简体中文一种：不弹语言选择，也不回落到英文 Default.isl。

#ifndef AppSourceDir
  #define AppSourceDir "..\publish\0.0.1"
#endif

#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

#define AppName "忆潮"
#define AppExeName "YiChao.exe"
#define AppPublisher "YiChao"

[Setup]
; AppId 保持不变：老版本可直接就地升级，不产生两份并存的安装记录
AppId={{9F8C0B3C-8E44-4E1A-A0D4-YICAO0000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} 安装程序
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=AGPL-3.0 (c) 2026 EvilgodXu
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\publish
OutputBaseFilename=YiChao-{#AppVersion}-setup
SetupIconFile={#AppSourceDir}\Assets\app.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 与 csproj 的 TargetPlatformMinVersion 一致：Windows 10 1809 (17763) 起
MinVersion=10.0.17763
DisableProgramGroupPage=yes
; 保留目录页：用户可自选安装位置
DisableDirPage=no
WizardStyle=modern
; 升级时若旧版仍在运行，按文件占用情况关闭它，避免覆盖失败
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
; 应用持有该互斥量：安装/卸载时能识别“正在运行”，提示用户而不是硬写文件
AppMutex=Local\YiChao.App.Instance
; 安装日志留在 %TEMP%：离线用户的现场问题可凭日志定位
SetupLogging=yes
LanguageDetectionMethod=none
ShowLanguageDialog=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
; 自包含运行时（.NET + Windows App SDK）、XAML 资源与资源文件整体铺开
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; AGPL-3.0 要求分发时随附许可证全文
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动{#AppName}"; Flags: nowait postinstall skipifsilent
