# 编译小克开机动画启动器：只用 Windows 自带的 .NET Framework 编译器，不下载任何东西
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

& $csc /nologo /codepage:65001 /optimize+ /target:winexe `
    /win32icon:"$here\clawd.ico" `
    /r:System.Web.Extensions.dll `
    /out:"$here\ClawdSplash.exe" `
    "$here\ClawdSplash.cs"
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "OK -> $here\ClawdSplash.exe"
