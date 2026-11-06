param(
    [string]$BridgeProject = 'src\MaestroOscService\GEXMaestroBridgeService.csproj',
    [string]$Version = '',
    [string]$HidMaestroDll = '',
    [string]$Iscc = '',
    [switch]$PublishRelease
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($BridgeProject)) { $BridgeProject = Join-Path $root $BridgeProject }
if (-not (Test-Path -LiteralPath $BridgeProject)) { throw "Bridge project not found: $BridgeProject" }
if (-not $Version) {
    $Version = (& dotnet msbuild $BridgeProject -nologo -getProperty:Version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the bridge project version.' }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Installer version must be major.minor.patch.' }
if (-not $HidMaestroDll) { $HidMaestroDll = Join-Path $root 'lib\HIDMaestro.Core.dll' }
if (-not [IO.Path]::IsPathRooted($HidMaestroDll)) { $HidMaestroDll = Join-Path $root $HidMaestroDll }
if (-not (Test-Path -LiteralPath $HidMaestroDll)) { throw 'Obtain the complete HIDMaestro 2.x SDK and place HIDMaestro.Core.dll in lib, or pass -HidMaestroDll.' }
if (-not $Iscc) { $Iscc = $env:ISCC_EXE }
if (-not $Iscc) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $Iscc = $compiler.Source }
}
if (-not $Iscc) {
    foreach ($folder in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        foreach ($edition in @('Inno Setup 7', 'Inno Setup 6')) {
            $candidate = Join-Path $folder "$edition\ISCC.exe"
            if (Test-Path -LiteralPath $candidate) { $Iscc = $candidate; break }
        }
        if ($Iscc) { break }
    }
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) { throw 'Install Inno Setup 6 or 7, set ISCC_EXE, or pass -Iscc.' }

$requiredLicenses = @('GPL-3.0.txt', 'HIDMaestro-MIT.txt', 'THIRD-PARTY-NOTICES.txt', 'README.md')
foreach ($name in $requiredLicenses) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "licenses\$name"))) { throw "Missing license file: $name" }
}
# A fresh staging directory prevents old executables or installers entering the payload.
$payload = Join-Path $root ('artifacts\installer-payload\' + $Version + '-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Path $payload, $output -Force | Out-Null
Write-Host "Publishing self-contained bridge $Version for Windows x64..."
& dotnet publish $BridgeProject -c Release -r win-x64 --self-contained true `
    "-p:Version=$Version" "-p:HidMaestroDll=$HidMaestroDll" -p:MockOnly=false `
    -p:BuildInstaller=false -p:PublishGitHubRelease=false -p:SkipBridgeBuildOutput=true `
    -p:PublishSingleFile=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $payload 'GEXMaestroBridgeService.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published bridge executable is missing.' }
$actualVersion = (& $exe --version | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $actualVersion -ne $Version) { throw "Published bridge version $actualVersion differs from installer $Version." }
foreach ($name in @('HIDMaestro.Core.dll', 'appsettings.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name))) { throw "Published dependency missing: $name" }
}
$licenseTarget = Join-Path $payload 'licenses'
New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
foreach ($name in $requiredLicenses) {
    Copy-Item -LiteralPath (Join-Path $root "licenses\$name") -Destination $licenseTarget -Force
}
& $Iscc "/DAppVersion=$Version" "/DPayloadDir=$payload" "/O$output" (Join-Path $PSScriptRoot 'GEXMaestroBridge.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installer = Join-Path $output "GEXMaestroBridgeSetup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Compiled installer is missing.' }
Write-Host "Installer built: $installer"
if ($PublishRelease) {
    & (Join-Path $PSScriptRoot 'publish-release.ps1') -Version $Version -Installer $installer
}
