# 给小克开机动画启动器建快捷方式（桌面 + 开始菜单）
#   安装：powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
#   卸载：powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -Uninstall
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$name = 'Claude（小克动画版）.lnk'
$targets = @(
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs')
) | ForEach-Object { Join-Path $_ $name }

if ($Uninstall) {
    $targets | Where-Object { Test-Path $_ } | ForEach-Object { Remove-Item $_; "removed $_" }
    return
}

# WScript.Shell 走系统 ANSI 代码页，中文文件名存不进去：先用英文临时名保存，再改名
$shell = New-Object -ComObject WScript.Shell
foreach ($t in $targets) {
    $tmp = Join-Path (Split-Path $t) 'ClawdSplash-tmp.lnk'
    $lnk = $shell.CreateShortcut($tmp)
    $lnk.TargetPath = Join-Path $here 'ClawdSplash.exe'
    $lnk.WorkingDirectory = $here
    $lnk.IconLocation = (Join-Path $here 'clawd.ico') + ',0'
    $lnk.Description = 'Clawd boot animation, then Claude'
    $lnk.Save()
    Move-Item -LiteralPath $tmp -Destination $t -Force
    "created $t"
}
