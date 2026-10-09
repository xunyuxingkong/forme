$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$formeTarget = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Forme'))
$formeScriptRoot = [IO.Path]::GetFullPath($PSScriptRoot)
try {
    if ($formeTarget -ne $formeScriptRoot -or -not (Test-Path -LiteralPath (Join-Path $formeTarget '.forme-install'))) {
        throw '无法确认安装目录，未执行删除。'
    }
    if ((Get-Item -LiteralPath $formeTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw '安装目录为链接，未执行删除。'
    }
    $formeChoice = [Windows.Forms.MessageBox]::Show('卸载 Forme？个人数据将保留，可在重新安装后继续使用。要清除个人数据，请先到应用设置中操作。', 'Forme', 'OKCancel', 'Information')
    if ($formeChoice -ne 'OK') { exit }
    foreach ($formeProcess in (Get-Process Forme -ErrorAction SilentlyContinue)) {
        if ($formeProcess.Path -eq (Join-Path $formeTarget 'Forme.exe')) { throw '请先从托盘退出 Forme，再卸载。' }
    }
    $formeShortcutPaths = @(
        (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Forme.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'Forme\Forme.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'Forme\卸载 Forme.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Startup')) 'Forme.lnk')
    )
    foreach ($formeShortcut in $formeShortcutPaths) { if (Test-Path -LiteralPath $formeShortcut) { Remove-Item -LiteralPath $formeShortcut } }
    $formeMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Forme'
    if ((Test-Path -LiteralPath $formeMenuDirectory) -and @(Get-ChildItem -LiteralPath $formeMenuDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $formeMenuDirectory }
    Remove-Item -LiteralPath $formeTarget -Recurse -Force
    $formeRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Forme'
    if (Test-Path -LiteralPath $formeRegistry) { Remove-Item -LiteralPath $formeRegistry }
    [Windows.Forms.MessageBox]::Show('Forme 已卸载。个人数据仍在当前用户的应用数据目录中。', 'Forme', 'OK', 'Information') | Out-Null
} catch {
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Forme', 'OK', 'Warning') | Out-Null
}
