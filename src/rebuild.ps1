# 编译脚本：生成 CodexQuotaTray.exe 并重新生成多尺寸 icon.ico
#   1. 先编译一个不带图标的 exe，用它生成 icon.ico
#   2. 再带图标编译一次
#
# 本脚本只做编译，不动系统上的任何东西：不结束进程、不建快捷方式、不自动启动。
# 想顺手刷新桌面快捷方式并启动，加 -UpdateShortcut。

param(
    [string]$OutName = 'CodexQuotaTray.exe',
    [switch]$UpdateShortcut
)

$ErrorActionPreference = 'Stop'

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $dir $OutName
$src = Join-Path $dir 'Program.cs'
$ico = Join-Path $dir 'icon.ico'

if (-not (Test-Path $csc)) {
    Write-Host '找不到 csc.exe（.NET Framework 4 编译器）。' -ForegroundColor Red
    Read-Host '按回车退出'
    exit 1
}

# 编译会占用 exe；只有本机自用刷新快捷方式时才会自动停掉旧实例
if ($UpdateShortcut) {
    Get-Process -Name 'CodexQuotaTray','额度显示器' -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "停止正在运行的实例 (pid=$($_.Id))"
        Stop-Process -Id $_.Id -Force
        Start-Sleep -Milliseconds 500
    }
}

$refs = @(
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Web.Extensions.dll'
)

# 把 quota.js 嵌进 exe：这样单文件 exe 就能跑，不用额外带 .js
$js = Join-Path $dir 'quota.js'
$resArgs = @()
if (Test-Path $js) { $resArgs = @("/resource:$js,quota.js") }

Push-Location $dir
try {
    Write-Host '[1/3] 编译（无图标版本，仅用于生成 ico）…'
    & $csc /nologo /target:winexe /out:$out @refs @resArgs $src 2>&1 |
        Where-Object { $_ -notmatch 'warning CS0649' } | ForEach-Object { Write-Host $_ }

    if (-not (Test-Path $out)) { throw '第一次编译失败，无法生成图标。' }

    Write-Host '[2/3] 生成多尺寸 icon.ico…'
    Start-Process -FilePath $out -ArgumentList @('--make-ico', $ico) -Wait -WindowStyle Hidden

    Write-Host '[3/3] 带图标重新编译…'
    & $csc /nologo /target:winexe /out:$out /win32icon:$ico @refs @resArgs $src 2>&1 |
        Where-Object { $_ -notmatch 'warning CS0649' } | ForEach-Object { Write-Host $_ }
}
finally {
    Pop-Location
}

if (-not (Test-Path $out)) {
    Write-Host '编译失败，请看上面的错误信息。' -ForegroundColor Red
    Read-Host '按回车退出'
    exit 1
}

Write-Host ''
Write-Host "编译完成：$out" -ForegroundColor Green

if (-not $UpdateShortcut) {
    Write-Host '（只做了编译。要顺带刷新桌面快捷方式并启动，加 -UpdateShortcut）'
    return
}

# 下面是本机自用才需要的动作，默认不做
$lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodexQuotaTray.lnk'
$sh = New-Object -ComObject WScript.Shell
$lnk = $sh.CreateShortcut($lnkPath)
$lnk.TargetPath = $out
$lnk.WorkingDirectory = $dir
$lnk.IconLocation = "$out,0"
$lnk.Description = 'Codex Quota Tray'
$lnk.Save()

Write-Host '正在启动…'
Start-Process -FilePath $out
Start-Sleep -Seconds 2
