# 编译正式分体托盘版本；保留现有icon.ico，不重新生成额度图标。
param(
    [string]$OutName = 'CodexQuotaTray.exe',
    [switch]$UpdateShortcut,
    [switch]$ToastPreview
)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Split-Path -Parent $dir
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $project 'build'
[IO.Directory]::CreateDirectory($build) | Out-Null
$out = Join-Path $build $OutName
$previewFlags = @()
if ($ToastPreview) {
    if ($UpdateShortcut) { throw '通知样本不能替换桌面额度显示器。' }
    $out = Join-Path $build 'preview\QuotaRecoveryPreview.exe'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $out)) | Out-Null
    $previewFlags = @('/define:QUOTA_TOAST_PREVIEW')
}
if (-not (Test-Path -LiteralPath $csc)) { throw '找不到.NET Framework 4编译器。' }
$sourceNames = @('Program.cs','Accounts.cs','MultiAccounts.cs','SettingsForm.cs','QuotaPreferences.cs','ToastSound.cs')
$sources = @($sourceNames | ForEach-Object { Join-Path $dir $_ })
foreach ($file in $sources) { if (-not (Test-Path -LiteralPath $file)) { throw "缺少源码：$file" } }
if ($UpdateShortcut) {
    Get-Process -Name 'CodexQuotaTray','额度显示器' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
$icon = Join-Path $dir 'icon.ico'
$quota = Join-Path $dir 'quota.js'
$accounts = Join-Path $dir 'accounts.js'
$soundDir = Join-Path $project '资源'
if (-not (Test-Path -LiteralPath $soundDir -PathType Container)) { $soundDir = $project }
$soundResources = foreach ($name in @('notify.wav','notify-5h-empty.wav','notify-7d-empty.wav','notify-restored.wav')) {
    $wav = Join-Path $soundDir $name
    if (-not (Test-Path -LiteralPath $wav -PathType Leaf)) { throw "缺少提示音：$wav" }
    "/resource:$wav,$name"
}
& $csc /nologo /target:winexe "/out:$out" "/win32icon:$icon" "/resource:$quota,quota.js" "/resource:$accounts,accounts.js" @soundResources @previewFlags /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败，旧程序未更新。' }
Write-Host "编译完成：$out"
if ($UpdateShortcut) {
    $linkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '额度显示器.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $out
    $link.WorkingDirectory = $build
    $link.IconLocation = "$out,0"
    $link.Save()
    Start-Process -FilePath $out
}
