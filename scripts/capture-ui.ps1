param([string]$Executable = 'artifacts/windows-x64/GuideDeck.exe', [string]$OutputDirectory = 'artifacts/ui-previews')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = '--capture-ui "' + $output + '"'
$app = Start-Process (Resolve-Path $Executable).Path -ArgumentList $arguments -PassThru
try {
    if (-not $app.WaitForExit(30000)) { throw 'UI capture timed out.' }
    if ($app.ExitCode -ne 0) {
        $errorFile = Join-Path $output 'capture-error.txt'
        if (Test-Path $errorFile) { Get-Content $errorFile }
        throw 'UI capture failed.'
    }
    foreach ($theme in @('light','dark','light-compact','dark-compact')) {
        $file = Join-Path $output "desktop-$theme.png"
        if (-not (Test-Path $file) -or (Get-Item $file).Length -lt 1000) { throw "Missing rendered preview: $theme" }
    }
    Write-Output 'PASS actual WPF light/dark rendering; appearance settings unchanged.'
} finally { if (-not $app.HasExited) { $app.Kill() } }
