# 一条命令产出离线安装包
#
# 依次做三件事：自包含发布 → 校验载荷里真的带了运行时 → 用中文消息文件编译安装器。
# 中间任何一步不满足就立刻失败，避免再产出“装完还要下载运行时”的包。

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    # 留空则从 csproj 的 <Version> 读取，避免版本号两处维护
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\YiChao.App\YiChao.App.csproj"
$publishRoot = Join-Path $repoRoot "publish"

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Get-VersionFromProject {
    $match = Select-String -Path $csproj -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
    if (-not $match) {
        throw "csproj 里找不到 <Version>，请显式传 -Version"
    }

    return $match.Matches[0].Groups[1].Value.Trim()
}

function Find-Iscc {
    # 允许用环境变量指定，其次是 PATH，最后按 Inno Setup 的常见安装位置找
    if ($env:ISCC -and (Test-Path $env:ISCC)) {
        return $env:ISCC
    }

    $onPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "找不到 ISCC.exe（Inno Setup 6）。装好后重试，或用环境变量 ISCC 指定路径。"
}

function Assert-SelfContained([string]$payload) {
    # 这三层缺任一层的表现都是“装完提示缺运行时”，所以在打包阶段就拦住
    $required = @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll")
    foreach ($name in $required) {
        if (-not (Test-Path (Join-Path $payload $name))) {
            throw "载荷缺少 $name：.NET 运行时没有随包，请确认以 -p:SelfContained=true 发布"
        }
    }

    $config = Join-Path $payload "YiChao.runtimeconfig.json"
    $text = Get-Content $config -Raw
    if ($text -notmatch '"includedFrameworks"') {
        throw "YiChao.runtimeconfig.json 仍是 framework 依赖模式（含 `"framework`" 字段）：发布时没有真正自包含"
    }

    if (-not (Test-Path (Join-Path $payload "Microsoft.WindowsAppRuntime.Bootstrap.dll"))) {
        throw "载荷缺少 Windows App SDK 自包含组件，请确认 WindowsAppSDKSelfContained=true"
    }
}

if (-not $Version) {
    $Version = Get-VersionFromProject
}

$payload = Join-Path $publishRoot $Version

Write-Step "检查是否有实例正在运行（会占用构建输出）"
$processes = Get-Process -Name "YiChao" -ErrorAction SilentlyContinue
# 线程数为 0 的是崩溃后遗留、正在等 WER 收尾的空壳，不占构建输出，只需提示
$running = @($processes | Where-Object { $_.Threads.Count -gt 0 })
$remnants = @($processes | Where-Object { $_.Threads.Count -eq 0 })
if ($running) {
    $paths = ($running | ForEach-Object { $_.Path }) -join "、"
    throw "忆潮正在运行（$paths），请先退出再打包：构建输出被占用会导致发布失败。"
}

if ($remnants) {
    Write-Warning "发现 $($remnants.Count) 个线程数为 0 的残留进程（崩溃后未回收），不影响本次打包，但建议重启系统清理。"
}

$iscc = Find-Iscc
Write-Step "版本 $Version；编译器 $iscc"

Write-Step "自包含发布到 $payload"
& dotnet publish (Join-Path $repoRoot "src\YiChao.App\YiChao.App.csproj") `
    -c $Configuration `
    -p:SelfContained=true `
    -o $payload
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（退出码 $LASTEXITCODE）"
}

Write-Step "校验载荷确实自带运行时"
Assert-SelfContained $payload
$size = [math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "    载荷 $size MB，运行时文件齐全" -ForegroundColor Green

Write-Step "编译安装器（简体中文界面）"
& $iscc (Join-Path $PSScriptRoot "setup.iss") "/DAppVersion=$Version" "/DAppSourceDir=$payload"
if ($LASTEXITCODE -ne 0) {
    throw "ISCC 编译失败（退出码 $LASTEXITCODE）"
}

$setup = Join-Path $publishRoot "YiChao-$Version-setup.exe"
if (-not (Test-Path $setup)) {
    throw "没有生成预期的安装包：$setup"
}

$setupSize = [math]::Round(((Get-Item $setup).Length / 1MB), 1)
Write-Step "完成"
Write-Host "    $setup（$setupSize MB）" -ForegroundColor Green
Write-Host "    自包含 .NET 运行时与 Windows App SDK，安装后无需联网下载任何组件。"
