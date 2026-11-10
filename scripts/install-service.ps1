#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$serviceName = 'GEXMaestroBridgeService'
$destination = 'C:\GremlinEx\GEXMaestroBridge'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'Service already exists. Stop/uninstall it before replacing the deployed files.'
}
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'GEXMaestroBridgeService.exe'))) {
    throw 'PublishDirectory must contain the published executable.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
# Protect executable/configuration because the service runs as LocalSystem.
& icacls.exe $destination /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /T | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the application directory.' }
$dataPath = Join-Path $env:ProgramData 'MaestroOscService'
New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
& icacls.exe $dataPath /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the definitions directory.' }
$binary = '"' + (Join-Path $destination 'GEXMaestroBridgeService.exe') + '"'
New-Service -Name $serviceName -DisplayName 'GEX Maestro Bridge Service' -BinaryPathName $binary -StartupType Automatic | Out-Null
Start-Service -Name $serviceName
Write-Host "Installed and started $serviceName. Requests: UDP 8010; reply destination: UDP 8011."
