param(
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = 'v0.4.1',
    [string]$ArchivePath,
    [string]$ChecksumFile,
    [switch]$SkipShortcut,
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'Programs/GuideDeck'
$markerName = '.guidedeck-install.json'
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
if (Get-Process -Name GuideDeck -ErrorAction SilentlyContinue) { throw 'Close GuideDeck before installing or updating. Steam and games will not be closed.' }
if ([bool]$ArchivePath -ne [bool]$ChecksumFile) { throw 'Offline installation requires both -ArchivePath and -ChecksumFile.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('GuideDeck-install-' + [guid]::NewGuid().ToString('N'))
$stage = $install + '.staging-' + [guid]::NewGuid().ToString('N')
$rollback = $install + '.previous-' + [guid]::NewGuid().ToString('N')
$swapped = $false
$committed = $false
function Assert-NotLink([string]$Path) {
    if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked directory: $Path" }
}
try {
    New-Item -ItemType Directory -Path $work | Out-Null
    if (-not $ArchivePath) {
        $base = "https://github.com/shenlee377/xbox-buttom-changer/releases/download/$Version"
        $ArchivePath = Join-Path $work 'GuideDeck-windows-x64.zip'
        $ChecksumFile = Join-Path $work 'SHA256SUMS.txt'
        Invoke-WebRequest "$base/GuideDeck-windows-x64.zip" -OutFile $ArchivePath -UseBasicParsing
        Invoke-WebRequest "$base/SHA256SUMS.txt" -OutFile $ChecksumFile -UseBasicParsing
    }
    $lines = @(Get-Content -LiteralPath $ChecksumFile | Where-Object { $_ -match '^[0-9a-fA-F]{64}\s+\*?GuideDeck-windows-x64\.zip$' })
    if ($lines.Count -ne 1) { throw 'Expected exactly one checksum for GuideDeck-windows-x64.zip.' }
    $expected = ($lines[0] -split '\s+')[0]
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expected) { throw 'Archive checksum mismatch. Nothing was installed.' }
    # Validate paths before extraction even though the archive checksum matched.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or $name.Split('/') -contains '..') { throw 'Unsafe archive path.' }
        }
    } finally { $zip.Dispose() }
    $expanded = Join-Path $work 'expanded'
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $expanded
    $payload = $expanded
    if (-not (Test-Path (Join-Path $payload 'GuideDeck.exe'))) { $payload = Join-Path $expanded 'GuideDeck' }
    if (-not (Test-Path (Join-Path $payload 'GuideDeck.exe'))) { throw 'Archive is missing GuideDeck.exe.' }
    if (-not (Test-Path (Join-Path $payload 'Uninstall-GuideDeck.ps1'))) { throw 'Archive is missing the uninstaller.' }
    Assert-NotLink (Split-Path $install -Parent)
    Assert-NotLink $install
    if (Test-Path $install) {
        $marker = Join-Path $install $markerName
        if (-not (Test-Path $marker) -or (Get-Content $marker -Raw | ConvertFrom-Json).Product -ne 'GuideDeck') { throw 'Existing directory was not installed by this script; refusing to overwrite it.' }
    }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $payload '*') $stage -Recurse
    @{Product='GuideDeck'; Version=$Version; InstalledAt=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content (Join-Path $stage $markerName) -Encoding UTF8
    if (Test-Path $install) { Move-Item -LiteralPath $install -Destination $rollback }
    try { Move-Item -LiteralPath $stage -Destination $install; $swapped = $true }
    catch { if (Test-Path $rollback) { Move-Item -LiteralPath $rollback -Destination $install }; throw }
    if (-not $SkipShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'GuideDeck.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $install 'GuideDeck.exe'; $shortcut.WorkingDirectory = $install; $shortcut.Save()
    }
    $committed = $true
    if (Test-Path $rollback) { Remove-Item -LiteralPath $rollback -Recurse -Force }
    Write-Output "Installed $Version to $install. Steam settings were not changed."
    if ($Launch) { Start-Process (Join-Path $install 'GuideDeck.exe') }
} catch {
    if ($swapped -and -not $committed) {
        $marker = Join-Path $install $markerName
        if ((Test-Path $marker) -and (Get-Content $marker -Raw | ConvertFrom-Json).Product -eq 'GuideDeck') { Remove-Item -LiteralPath $install -Recurse -Force }
        if ((Test-Path $rollback) -and -not (Test-Path $install)) { Move-Item -LiteralPath $rollback -Destination $install }
    }
    throw
} finally {
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (-not $swapped -and (Test-Path $rollback) -and -not (Test-Path $install)) { Move-Item -LiteralPath $rollback -Destination $install }
}
