param([string]$Version = '0.5.0', [string]$OutputDirectory = 'artifacts/installer')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Velopack tool restore failed.' }
dotnet tool run vpk -- pack --packId GuideDeck.Desktop --packVersion $Version --packDir artifacts/windows-x64 --mainExe GuideDeck.exe --outputDir $OutputDirectory --channel win-x64 --runtime win-x64 --packTitle GuideDeck --packAuthors shenlee377 --icon src/GuideDeck.Desktop/Assets/GuideDeck.ico --delta None
if ($LASTEXITCODE -ne 0) { throw 'Velopack installer packaging failed.' }
