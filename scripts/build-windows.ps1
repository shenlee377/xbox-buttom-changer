param([switch]$IncludeGameBar)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet run --project tests/GuideDeck.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet publish src/GuideDeck.Desktop -c Release -r win-x64 --self-contained true -o artifacts/windows-x64
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    Copy-Item README.txt artifacts/windows-x64/README.txt
    Copy-Item LICENSE artifacts/windows-x64/LICENSE
    Compress-Archive -Path artifacts/windows-x64/* -DestinationPath artifacts/GuideDeck-windows-x64.zip -Force
    if ($IncludeGameBar) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path $vswhere)) { throw 'Visual Studio MSBuild with UWP tools is required.' }
        $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not $msbuild) { throw 'MSBuild not found.' }
        & $msbuild src/GuideDeck.GameBar/GuideDeck.GameBar.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /p:AppxPackageSigningEnabled=false
        if ($LASTEXITCODE -ne 0) { throw 'Game Bar build failed. Verify the UWP workload and Windows SDK 19041 are installed.' }
    }
} finally { Pop-Location }
