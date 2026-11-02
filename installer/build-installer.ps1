param(
 [string]$BridgeProject='src/MaestroOscService/MaestroOscService.csproj',
 [string]$Version='0.1.0',
 [Parameter(Mandatory)][string]$HidZipSha256,
 [string]$Iscc='C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$payload=Join-Path $root 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
$zip=Join-Path $root 'HIDMaestro-v2.1.0.zip'
Invoke-WebRequest -Uri 'https://github.com/hifihedgehog/HIDMaestro/releases/download/v2.1.0/HIDMaestro-v2.1.0.zip' -OutFile $zip
$actual=(Get-FileHash $zip -Algorithm SHA256).Hash
if ($actual -ne $HidZipSha256) { throw "HIDMaestro checksum mismatch: $actual" }
$unpack=Join-Path $root 'hid-extracted'
Expand-Archive $zip -DestinationPath $unpack -Force
$dll=Get-ChildItem $unpack -Recurse -File -Filter 'HIDMaestro.Core.dll' | Select-Object -First 1
if (-not $dll) { throw 'HIDMaestro.Core.dll not found in release' }
Copy-Item $dll.FullName $payload
$licenseSource=Join-Path $root 'licenses'
$licenseTarget=Join-Path $payload 'licenses'
$required=@('GPL-3.0.txt','HIDMaestro-MIT.txt','THIRD-PARTY-NOTICES.txt','README.md')
foreach($name in $required) { if(-not (Test-Path (Join-Path $licenseSource $name))) { throw "Missing license file: $name" } }
New-Item -ItemType Directory -Force -Path $licenseTarget | Out-Null
foreach($name in $required) { Copy-Item (Join-Path $licenseSource $name) (Join-Path $licenseTarget $name) -Force }

& dotnet publish $BridgeProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $payload
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
if (-not (Test-Path (Join-Path $payload 'MaestroOscService.exe'))) { throw 'Bridge executable missing' }
foreach($name in $required) { if(-not (Test-Path (Join-Path $licenseTarget $name))) { throw "License not staged: $name" } }
if (-not (Test-Path $Iscc)) { throw 'Install Inno Setup 6 first' }
& $Iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'GEXMaestroBridge.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }
