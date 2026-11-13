# 改完 Program.cs 后，双击本文件（或用 PowerShell 运行）即可重新编译
#   1. 先编译一个不带图标的 exe，用它生成多尺寸 icon.ico
#   2. 再带图标编译一次（exe 和桌面快捷方式都因此换上新图标）
#   3. 刷新桌面快捷方式并启动
$ErrorActionPreference = 'Stop'

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $dir '额度显示器.exe'
$src = Join-Path $dir 'Program.cs'
$ico = Join-Path $dir 'icon.ico'

if (-not (Test-Path $csc)) {
    Write-Host '找不到 csc.exe（.NET Framework 4 编译器）。' -ForegroundColor Red
    Read-Host '按回车退出'
    exit 1
}

# 程序正在跑就先停掉，否则 exe 被占用无法覆盖
Get-Process -Name '额度显示器' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "停止正在运行的实例 (pid=$($_.Id))"
    Stop-Process -Id $_.Id -Force
    Start-Sleep -Milliseconds 500
}

$refs = @(
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Web.Extensions.dll'
)

# 把 quota.js 嵌进 exe：这样单文件 exe 就能跑，别人不用额外带 .js
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

# 让桌面快捷方式立刻用上新图标（重写一遍 lnk + 通知 shell 刷新）
try {
    $lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '额度显示器.lnk'
    $sh = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut($lnkPath)
    $lnk.TargetPath = $out
    $lnk.WorkingDirectory = $dir
    $lnk.IconLocation = "$out,0"
    $lnk.Description = 'Codex 额度显示器（右下角托盘）'
    $lnk.Save()

    if (-not ('Win.ShellNotify' -as [type])) {
        Add-Type -Namespace Win -Name ShellNotify -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, System.IntPtr item1, System.IntPtr item2);'
    }
    [Win.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
}
catch {
    Write-Host "快捷方式图标刷新失败（不影响使用）：$($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ''
Write-Host '编译成功，正在启动…' -ForegroundColor Green
Start-Process -FilePath $out
Start-Sleep -Seconds 2
