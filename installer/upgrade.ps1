param(
 [Parameter(Mandatory)][string]$InstallDir,
 [ValidateSet('prepare','rollback','complete')][string]$Action,
 [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference='Stop'
$service='GEXMaestroBridgeService'
$stateDir=Join-Path $env:ProgramData 'GEXMaestroBridge\upgrade'
$stateFile=Join-Path $stateDir 'state.json'
function StopBridge {
 $svc=Get-Service $service -ErrorAction SilentlyContinue
 if($svc -and $svc.Status -ne 'Stopped') {
  Stop-Service $service -Force -ErrorAction Stop
  (Get-Service $service).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(60))
 }
 $processes=@(Get-CimInstance Win32_Process -Filter "Name = 'GEXMaestroBridgeService.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($InstallDir,[StringComparison]::OrdinalIgnoreCase) })
 if($processes.Count) { throw 'Close the running bridge console application before upgrading.' }
}
if($Action -eq 'prepare') {
 if(Test-Path $stateFile) { throw 'An earlier upgrade is pending. Resolve or roll it back first.' }
 $existing=Join-Path $InstallDir 'GEXMaestroBridgeService.exe'
 if(-not (Test-Path $existing)) { exit 0 }
 $oldVersion=(& $existing --version | Select-Object -Last 1).Trim()
 if($LASTEXITCODE -ne 0 -or $oldVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Cannot determine installed bridge version.' }
 if([version]$Version -le [version]$oldVersion) { throw "Refusing same-version install or downgrade ($oldVersion -> $Version)." }
 $svc=Get-Service $service -ErrorAction SilentlyContinue
 $wasRunning=($svc -and $svc.Status -eq 'Running')
 StopBridge
 New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
 $backup=Join-Path $stateDir 'backup'
 if(Test-Path $backup) { Remove-Item $backup -Recurse -Force }
 New-Item -ItemType Directory -Force -Path $backup | Out-Null
 Copy-Item (Join-Path $InstallDir '*') $backup -Recurse -Force
 @{WasRunning=[bool]$wasRunning;OldVersion=$oldVersion;InstallDir=$InstallDir} | ConvertTo-Json | Set-Content $stateFile -Encoding UTF8
} elseif($Action -eq 'rollback') {
 if(-not (Test-Path $stateFile)) { exit 0 }
 $state=Get-Content $stateFile -Raw | ConvertFrom-Json
 StopBridge
 Copy-Item (Join-Path $stateDir 'backup\*') $state.InstallDir -Recurse -Force
 if($state.WasRunning) { Start-Service $service }
 Remove-Item $stateDir -Recurse -Force
} elseif($Action -eq 'complete') {
 if(Test-Path $stateDir) { Remove-Item $stateDir -Recurse -Force }
}
