$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run only on disposable GitHub Actions runners.' }
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$install = Join-Path $env:LOCALAPPDATA 'GuideDeck.Desktop'
$data = Join-Path $env:LOCALAPPDATA 'GuideDeck'
$log = Join-Path $repo 'artifacts/update-test'
New-Item $log -ItemType Directory -Force | Out-Null
function Wait-For([scriptblock]$Condition, [string]$Description) {
    $limit = (Get-Date).AddSeconds(60)
    while (-not (& $Condition)) {
        if ((Get-Date) -gt $limit) { throw "Timed out: $Description" }
        Start-Sleep -Milliseconds 500
    }
}
# Setup --silent is the documented no-launch option.
$setup = @(Get-ChildItem artifacts/installer -Filter '*Setup.exe')
if ($setup.Count -ne 1) { throw 'Expected one setup.' }
$process = Start-Process $setup[0].FullName -ArgumentList '--silent' -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Setup failed: $($process.ExitCode)" }
$exe = Join-Path $install 'current/GuideDeck.exe'
if (-not (Test-Path $exe)) { throw 'Installed executable missing.' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$registration = @(Get-ChildItem $registry | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.DisplayName -eq 'GuideDeck' })
if ($registration.Count -ne 1 -or -not $registration[0].UninstallString) { throw 'Windows uninstall registration missing.' }
$registration | Select-Object DisplayName,DisplayVersion,UninstallString | ConvertTo-Json | Set-Content "$log/registration.json"
New-Item $data -ItemType Directory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'update-test-marker.txt'), 'preserve across update')
./scripts/pack-installer.ps1 -Version '0.5.1' -OutputDirectory 'artifacts/update-fixture'
$fixture = Join-Path $repo 'artifacts/update-fixture'
$package = @(Get-ChildItem $fixture -Filter '*-full.nupkg')
if ($package.Count -ne 1) { throw 'Expected one next-version fixture.' }
$originalHash = (Get-FileHash $package[0].FullName).Hash
# A damaged payload must fail verification and never advance the installed version.
$originalPackage = Join-Path $env:TEMP 'guidedeck-original-update.nupkg'
Copy-Item $package[0].FullName $originalPackage -Force
[IO.File]::WriteAllText($package[0].FullName, 'damaged update')
$badResult = Join-Path $log 'corrupt.json'
$p = Start-Process $exe -ArgumentList @('--ci-update-apply', ('"' + $fixture + '"'), ('"' + $badResult + '"')) -PassThru -Wait
if ($p.ExitCode -ne 1 -or -not (Test-Path $badResult)) { throw 'Corrupt update was not rejected.' }
if (-not ((Get-Content $badResult -Raw | ConvertFrom-Json).Error)) { throw 'Expected checksum rejection error.' }
Copy-Item $originalPackage $package[0].FullName -Force
if ((Get-FileHash $package[0].FullName).Hash -ne $originalHash) { throw 'Fixture restore failed.' }
$goodResult = Join-Path $log 'updated.json'
$p = Start-Process $exe -ArgumentList @('--ci-update-apply', ('"' + $fixture + '"'), ('"' + $goodResult + '"')) -PassThru
Wait-For { Test-Path $goodResult } 'updated process restart'
$result = Get-Content $goodResult -Raw | ConvertFrom-Json
if ($result.Version -ne '0.5.1' -or $result.Pending) { throw "Installed update failed: $(Get-Content $goodResult -Raw)" }
if ((Get-Content (Join-Path $data 'update-test-marker.txt') -Raw) -ne 'preserve across update') { throw 'Update lost user data.' }
# Verify uninstall deletes only app data, without traversing junctions.
$outside = Join-Path $env:TEMP 'guidedeck-outside-cleanup'
New-Item $outside -ItemType Directory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'keep.txt'), 'do not delete')
New-Item -Path (Join-Path $data 'external-link') -ItemType Junction -Value $outside | Out-Null
$update = Join-Path $install 'Update.exe'
$p = Start-Process $update -ArgumentList @('uninstall', '--silent') -PassThru -Wait
if ($p.ExitCode -ne 0) { throw "Uninstall failed: $($p.ExitCode)" }
Wait-For { -not (Test-Path (Join-Path $install 'current')) } 'installed files removed'
if (Test-Path $data) { throw 'Uninstall left GuideDeck data behind.' }
if (-not (Test-Path (Join-Path $outside 'keep.txt'))) { throw 'Cleanup traversed an external junction.' }
$remaining = @(Get-ChildItem $registry | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.DisplayName -eq 'GuideDeck' })
if ($remaining.Count -ne 0) { throw 'Uninstall left Windows app registration behind.' }
[IO.File]::WriteAllText((Join-Path $log 'SUCCESS.txt'), 'Installed, registered, rejected corrupt package, updated to 0.5.1, restarted, preserved data during update, removed app/data/registration on uninstall, preserved junction target.')
