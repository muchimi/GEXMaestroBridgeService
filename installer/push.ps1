param(
	[ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '',
	[string]$Installer = ''
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishScript = Join-Path $scriptDir 'publish-release.ps1'

if (-not (Test-Path -LiteralPath $publishScript)) {
	throw "Missing script: $publishScript"
}

Write-Host 'Publishing installer release to GitHub...'
if ($Version -and $Installer) {
	& $publishScript -Version $Version -Installer $Installer
} elseif ($Version) {
	& $publishScript -Version $Version
} elseif ($Installer) {
	& $publishScript -Installer $Installer
} else {
	& $publishScript
}

if ($LASTEXITCODE -ne 0) {
	throw "publish-release.ps1 failed with exit code $LASTEXITCODE"
}

Write-Host 'Done.'
