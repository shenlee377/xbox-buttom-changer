$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'Programs/GuideDeck'
if (Get-Process -Name GuideDeck -ErrorAction SilentlyContinue) { throw 'Close GuideDeck before uninstalling.' }
if (-not (Test-Path $install)) { Write-Output 'GuideDeck is not installed.'; return }
if ((Get-Item -LiteralPath $install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove a linked directory.' }
$marker = Join-Path $install '.guidedeck-install.json'
if (-not (Test-Path $marker) -or (Get-Content $marker -Raw | ConvertFrom-Json).Product -ne 'GuideDeck') { throw 'Installation marker missing or invalid. Nothing removed.' }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'GuideDeck.lnk'
if (Test-Path $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcutPath).TargetPath -eq (Join-Path $install 'GuideDeck.exe')) { Remove-Item -LiteralPath $shortcutPath }
}
Remove-Item -LiteralPath $install -Recurse -Force
Write-Output 'GuideDeck removed. Backups/reports and Steam settings were preserved. Restore Steam mappings inside GuideDeck before uninstalling if desired.'
