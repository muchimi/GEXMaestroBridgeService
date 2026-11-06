# Verify installer configuration with mocked Windows service/firewall operations.
$ErrorActionPreference = 'Stop'
$script:Checks = 0
$testState = @{}
function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:Checks++
}
function Get-Service { param($Name, $ErrorAction) return $testState.ExistingService }
function New-Service {
    param($Name, $DisplayName, $BinaryPathName, $StartupType)
    $testState.CreatedService = @{Name=$Name; Binary=$BinaryPathName; Startup=$StartupType}
}
function Start-Service { param($Name) $testState.StartedService = $Name }
function Stop-Service { param($Name, [switch]$Force) $testState.StoppedService = $Name }
function Get-NetFirewallRule { param($Name, $ErrorAction) return @() }
function Remove-NetFirewallRule { }
function New-NetFirewallRule {
    param($Name, $DisplayName, $Direction, $Action, $Protocol, $LocalPort, $RemoteAddress, $Profile, $Program)
    $testState.Firewall = @{Name=$Name; Port=$LocalPort; Remote=$RemoteAddress; Program=$Program}
}
function Get-CimInstance { param($ClassName, $Filter) return [pscustomobject]@{Name='GEXMaestroBridgeService'} }
function Invoke-CimMethod {
    param($InputObject, $MethodName, $Arguments)
    $testState.ServiceChange = $Arguments
    return [pscustomobject]@{ReturnValue=0}
}
function sc.exe { $testState.DeletedService = $args[1]; $global:LASTEXITCODE = 0 }

$stage = Join-Path ([IO.Path]::GetTempPath()) ('GEX Bridge configure test ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$exe = Join-Path $stage 'GEXMaestroBridgeService.exe'
[IO.File]::WriteAllBytes($exe, (New-Object byte[] 1))
$configure = Join-Path $PSScriptRoot 'configure.ps1'
try {
    $testState.ExistingService = $null
    & $configure -InstallDir $stage
    Assert-True ($testState.CreatedService.Name -eq 'GEXMaestroBridgeService') 'Register the service name expected by the application and dependency detector.'
    Assert-True ($testState.CreatedService.Binary -eq ('"' + $exe + '"')) 'Quote the actual executable path, including spaces.'
    Assert-True ($testState.CreatedService.Startup -eq 'Automatic') 'Start the service with Windows.'
    Assert-True ($testState.StartedService -eq 'GEXMaestroBridgeService') 'Start the newly installed service.'
    $settings = Get-Content -LiteralPath (Join-Path $stage 'appsettings.json') -Raw | ConvertFrom-Json
    Assert-True ($settings.Osc.ListenAddress -eq '127.0.0.1') 'Bind local mode to loopback.'

    $testState.CreatedService = $null
    $testState.StartedService = $null
    & $configure -InstallDir $stage -Mode console
    Assert-True (-not $testState.CreatedService -and -not $testState.StartedService) 'Console mode must not create or start a service.'

    $testState.ExistingService = [pscustomobject]@{Status='Stopped'}
    & $configure -InstallDir $stage -Remote true -GremlinHost 192.0.2.10 -FirewallScope host
    Assert-True ($testState.ServiceChange.PathName -eq ('"' + $exe + '"')) 'Update an existing service to the packaged executable.'
    Assert-True ($testState.Firewall.Remote -eq '192.0.2.10' -and $testState.Firewall.Port -eq 8010) 'Limit remote mode firewall access to the selected host and port.'
    & $configure -InstallDir $stage -Mode console
    Assert-True ($testState.DeletedService -eq 'GEXMaestroBridgeService') 'Remove the canonical service when switching to console mode.'
} finally {
    foreach ($name in @('GEXMaestroBridgeService.exe','appsettings.json','appsettings.json.backup')) {
        Remove-Item -LiteralPath (Join-Path $stage $name) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $stage -Force
}
Write-Host "Passed $script:Checks configuration checks. No Windows services or firewall rules were changed."
