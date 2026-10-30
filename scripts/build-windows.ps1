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
    Copy-Item VALIDATION.txt artifacts/windows-x64/VALIDATION.txt
    Copy-Item THIRD-PARTY-NOTICES.txt artifacts/windows-x64/THIRD-PARTY-NOTICES.txt
    Copy-Item scripts/Uninstall-GuideDeck.ps1 artifacts/windows-x64/Uninstall-GuideDeck.ps1
    Compress-Archive -Path artifacts/windows-x64/* -DestinationPath artifacts/GuideDeck-windows-x64.zip -Force
    if ($IncludeGameBar) {
        $companion = Join-Path $repo 'src/GuideDeck.GameBar/Companion'
        if (Test-Path $companion) { Remove-Item $companion -Recurse -Force }
        New-Item -ItemType Directory -Path $companion | Out-Null
        Copy-Item artifacts/windows-x64/* $companion -Recurse
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path $vswhere)) { throw 'Visual Studio MSBuild with UWP tools is required.' }
        $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not $msbuild) { throw 'MSBuild not found.' }
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Include'
        $sdkVersion = Get-ChildItem $sdkRoot -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
        if (-not $sdkVersion) { throw 'Windows SDK 19041 or later is required.' }
        & $msbuild src/GuideDeck.GameBar/GuideDeck.GameBar.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /p:AppxPackageSigningEnabled=false "/p:TargetPlatformVersion=$sdkVersion" /p:GenerateAppxPackageOnBuild=true
        if ($LASTEXITCODE -ne 0) { throw 'Game Bar build failed. Verify the UWP workload and Windows SDK 19041 are installed.' }
    }
    $hash = (Get-FileHash artifacts/GuideDeck-windows-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $repo 'artifacts/SHA256SUMS.txt'), "$hash  GuideDeck-windows-x64.zip`n", [Text.UTF8Encoding]::new($false))
} finally { Pop-Location }
