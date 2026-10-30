#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$serviceName = 'MaestroOscService'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(180)) }
    & sc.exe delete $serviceName
    if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
Write-Host 'Service removed. Published files and saved device definitions are retained.'
