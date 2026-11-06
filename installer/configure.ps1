param(
 [Parameter(Mandatory)][string]$InstallDir,
 [ValidateSet('service','console')][string]$Mode='service',
 [ValidateRange(1,65535)][int]$ListenPort=8010,
 [ValidateRange(1,65535)][int]$SendPort=8011,
 [ValidateSet('true','false')][string]$Remote='false',
 [string]$GremlinHost='127.0.0.1',
 [ValidateSet('none','host','any')][string]$FirewallScope='none',
 [ValidateSet('true','false')][string]$CreateDevices='false'
)
$ErrorActionPreference='Stop'
$exe=Join-Path $InstallDir 'GEXMaestroBridgeService.exe'
if (!(Test-Path $exe)) { throw "Missing executable: $exe" }
if ($Remote -eq 'true') {
 $ip=$null
 if (-not [Net.IPAddress]::TryParse($GremlinHost,[ref]$ip) -or $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { throw 'Enter a valid IPv4 address.' }
 $listen='0.0.0.0'; $reply=$GremlinHost; $allowed=@($GremlinHost)
} else {
 $listen='127.0.0.1'; $reply='127.0.0.1'; $allowed=@('127.0.0.1'); $FirewallScope='none'
}
$cfg=Join-Path $InstallDir 'appsettings.json'
$settings=if(Test-Path $cfg) { Get-Content $cfg -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $settings.PSObject.Properties['Osc']) { $settings | Add-Member NoteProperty Osc ([pscustomobject]@{}) }
foreach($entry in @{ListenAddress=$listen;ReceivePort=$ListenPort;SendPort=$SendPort;ReplyAddress=$reply;AllowedSenders=$allowed}.GetEnumerator()) {
 if($settings.Osc.PSObject.Properties[$entry.Key]) { $settings.Osc.($entry.Key)=$entry.Value }
 else { $settings.Osc | Add-Member NoteProperty $entry.Key $entry.Value }
}
if(Test-Path $cfg) { Copy-Item $cfg "$cfg.backup" -Force }
$settings | ConvertTo-Json -Depth 20 | Set-Content $cfg -Encoding UTF8
$service='GEXMaestroBridgeService'
$existing=Get-Service $service -ErrorAction SilentlyContinue
if($existing) {
 if($existing.Status -ne 'Stopped') { Stop-Service $service -Force }
 if($Mode -eq 'console') {
  & sc.exe delete $service | Out-Null
  if($LASTEXITCODE -ne 0) { throw 'Could not remove the existing bridge service.' }
 }
}
if($Mode -eq 'service') {
 $binary='"'+$exe+'"'
 if(-not $existing) {
  New-Service -Name $service -DisplayName 'GEX Maestro Bridge Service' -BinaryPathName $binary -StartupType Automatic | Out-Null
 } else {
  $serviceConfig=Get-CimInstance Win32_Service -Filter "Name='$service'"
  $result=Invoke-CimMethod -InputObject $serviceConfig -MethodName Change -Arguments @{PathName=$binary;StartMode='Automatic'}
  if($result.ReturnValue -ne 0) { throw "Could not configure the bridge service: $($result.ReturnValue)" }
 }
}
Get-NetFirewallRule -Name 'GEXMaestroBridge-OSC' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if($FirewallScope -ne 'none') {
 $source=if($FirewallScope -eq 'any'){'Any'}else{$GremlinHost}
 New-NetFirewallRule -Name 'GEXMaestroBridge-OSC' -DisplayName 'GEX Maestro Bridge OSC' -Direction Inbound -Action Allow -Protocol UDP -LocalPort $ListenPort -RemoteAddress $source -Profile Private -Program $exe | Out-Null
}
if($CreateDevices -eq 'true') { Write-Warning 'GEX 1-4 provisioning is not implemented. Devices were NOT created.' }
if($Mode -eq 'service') { Start-Service $service }
