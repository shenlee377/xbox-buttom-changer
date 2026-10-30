param([string]$Executable = 'artifacts/windows-x64/GuideDeck.exe')
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This registry-change test is restricted to isolated GitHub Actions runners.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GuideDeckThemeSignal {
 [DllImport("user32.dll", CharSet=CharSet.Unicode)]
 public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
'@
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Themes\Personalize')
$hadValue = $key.GetValueNames() -contains 'AppsUseLightTheme'
$previous = $key.GetValue('AppsUseLightTheme')
$kind = if ($hadValue) { $key.GetValueKind('AppsUseLightTheme') } else { [Microsoft.Win32.RegistryValueKind]::DWord }
$probe = Join-Path $env:TEMP ('guidedeck-theme-' + [guid]::NewGuid() + '.json')
$appearance = Join-Path $env:LOCALAPPDATA 'GuideDeck/appearance.json'
$appearanceBefore = if (Test-Path $appearance) { [IO.File]::ReadAllBytes($appearance) } else { $null }
function Wait-Theme([string]$Mode, [string]$Variant) {
    $matched = $false; $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        try { $state = Get-Content $probe -Raw | ConvertFrom-Json; $matched = $state.Mode -eq $Mode -and $state.Variant -eq $Variant } catch { }
    } while (-not $matched -and [DateTime]::UtcNow -lt $limit)
    if (-not $matched) { throw "Expected $Mode/$Variant in running app." }
}
$app = $null
try {
    $app = Start-Process (Resolve-Path $Executable).Path -ArgumentList ('--theme-probe "' + $probe + '"') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do { Start-Sleep -Milliseconds 200; $app.Refresh() } while (-not $app.HasExited -and $app.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($app.HasExited -or $app.MainWindowHandle -eq [IntPtr]::Zero) { throw 'No desktop window for theme test.' }
    foreach ($variant in @('Light','Dark')) {
        $value = if ($variant -eq 'Light') { 1 } else { 0 }
        $key.SetValue('AppsUseLightTheme', $value, [Microsoft.Win32.RegistryValueKind]::DWord)
        $messageResult = [UIntPtr]::Zero
        $null = [GuideDeckThemeSignal]::SendMessageTimeout($app.MainWindowHandle, 0x001A, [UIntPtr]::Zero, 'ImmersiveColorSet', 2, 1000, [ref]$messageResult)
        Wait-Theme 'System' $variant
    }
    # Simulate the same-package widget writing a manual preference.
    [IO.File]::WriteAllText($appearance, '{"Mode":"Light"}', [Text.UTF8Encoding]::new($false))
    Wait-Theme 'Light' 'Light'
    [IO.File]::WriteAllText($appearance, '{"Mode":"Dark"}', [Text.UTF8Encoding]::new($false))
    Wait-Theme 'Dark' 'Dark'
    $key.SetValue('AppsUseLightTheme', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
    $messageResult = [UIntPtr]::Zero
    $null = [GuideDeckThemeSignal]::SendMessageTimeout($app.MainWindowHandle, 0x001A, [UIntPtr]::Zero, 'ImmersiveColorSet', 2, 1000, [ref]$messageResult)
    Wait-Theme 'Dark' 'Dark'
    Write-Output 'PASS live system themes, shared appearance changes and manual override.'
} finally {
    if ($hadValue) { $key.SetValue('AppsUseLightTheme', $previous, $kind) } else { $key.DeleteValue('AppsUseLightTheme', $false) }
    $key.Close()
    if ($app -and -not $app.HasExited) { $null = $app.CloseMainWindow(); if (-not $app.WaitForExit(5000)) { $app.Kill() } }
    if ($null -ne $appearanceBefore) { [IO.File]::WriteAllBytes($appearance, $appearanceBefore) } else { Remove-Item $appearance -ErrorAction SilentlyContinue }
    Remove-Item $probe -ErrorAction SilentlyContinue
}
