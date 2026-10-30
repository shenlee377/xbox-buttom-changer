param([string]$Executable = 'artifacts/windows-x64/GuideDeck.exe')
$ErrorActionPreference = 'Stop'
$first = $null; $second = $null
$started = Get-Date
try {
    $first = Start-Process (Resolve-Path $Executable).Path -PassThru
    Start-Sleep -Seconds 4
    if ($first.HasExited) { throw 'Desktop app exited during startup.' }
    $second = Start-Process (Resolve-Path $Executable).Path -PassThru
    if (-not $second.WaitForExit(10000)) { throw 'Second instance did not exit.' }
    if ($second.ExitCode -ne 0 -or $first.HasExited) {
        Write-Output ("First exited: " + $first.HasExited + "; second exit code: " + $second.ExitCode)
        Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; StartTime=$started} -MaxEvents 3 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output $_.Message }
        throw 'Single-instance activation failed.'
    }
    Write-Output 'PASS desktop startup and second-instance activation.'
} finally {
    foreach ($process in @($first, $second)) {
        if ($process -and -not $process.HasExited) { $null = $process.CloseMainWindow(); if (-not $process.WaitForExit(5000)) { $process.Kill() } }
    }
}
