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
; 不交给 Restart Manager 自动关闭：应用常驻托盘时它只会隐藏窗口、关不掉，
; 安装器便卡在“无法自动关闭应用”。改由 [Code] 发退出事件请应用自己收尾。
CloseApplications=no
RestartApplications=no
; 安装日志留在 %TEMP%：离线用户的现场问题可凭日志定位
SetupLogging=yes
LanguageDetectionMethod=none
ShowLanguageDialog=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
; 自包含运行时（.NET + Windows App SDK）、XAML 资源与资源文件整体铺开。
; restartreplace：遇到被占用（例如上次崩溃残留的空壳进程仍映射着运行时 DLL）时
; 登记到重启后替换，而不是弹“删除文件失败：拒绝访问”把安装卡死。
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace
; AGPL-3.0 要求分发时随附许可证全文
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动{#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  // 与 SingleInstance.cs 里的命名一致：互斥量存在即代表有实例在跑
  AppMutexName = 'Local\YiChao.App.Instance';
  QuitEventName = 'Local\YiChao.App.Quit';
  SYNCHRONIZE = $00100000;
  EVENT_MODIFY_STATE = $0002;

function OpenMutexW(desiredAccess: LongWord; inheritHandle: Boolean; name: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function OpenEventW(desiredAccess: LongWord; inheritHandle: Boolean; name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function AppIsRunning: Boolean;
var
  handle: THandle;
begin
  handle := OpenMutexW(SYNCHRONIZE, False, AppMutexName);
  Result := handle <> 0;
  if Result then
    CloseHandle(handle);
end;

{ 请求应用自己退出并等它放掉互斥量；超时返回 False，由调用方给出可操作的提示 }
function RequestAppQuit: Boolean;
var
  handle: THandle;
  attempt: Integer;
begin
  handle := OpenEventW(EVENT_MODIFY_STATE, False, QuitEventName);
  if handle <> 0 then
  begin
    SetEvent(handle);
    CloseHandle(handle);
  end;

  for attempt := 1 to 80 do
  begin
    if not AppIsRunning then
    begin
      Result := True;
      Exit;
    end;
    Sleep(100);
  end;

  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not AppIsRunning then
    Exit;

  if RequestAppQuit then
    Exit;

  Result := '忆潮仍在后台运行，安装程序没能让它退出。' + #13#10 + #13#10 +
            '请右键点击任务栏通知区的「忆潮」图标，选择「退出」，再重新运行安装程序。';
end;
