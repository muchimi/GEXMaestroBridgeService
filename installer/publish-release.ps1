param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Installer
)
$ErrorActionPreference = 'Stop'
$script:Repository = 'muchimi/GEXMaestroBridgeService'

function Get-GitHubToken {
    foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { return $value }
    }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.Arguments = 'credential fill'
    $info.WorkingDirectory = Split-Path -Parent $PSScriptRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $info.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        $process.Start() | Out-Null
        $process.StandardInput.Write("protocol=https`nhost=github.com`npath=$script:Repository.git`n`n")
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Git credential lookup timed out.' }
        $credentials = $output.GetAwaiter().GetResult()
        if ($process.ExitCode -eq 0 -and $credentials -match '(?m)^password=(.+)\r?$') { return $Matches[1].Trim() }
    } finally { $process.Dispose() }
    throw 'Set GH_TOKEN/GITHUB_TOKEN with Contents write access, or sign in through Git Credential Manager.'
}

function Invoke-GitHubRequest {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Asset = '')
    $options = @{
        Method = $Method
        Uri = "https://api.github.com/repos/$script:Repository/$Path"
        Headers = $script:GitHubHeaders
        TimeoutSec = 600
    }
    if ($Asset) {
        $options.Uri = "https://uploads.github.com/repos/$script:Repository/$Path"
        $options.InFile = $Asset
        $options.ContentType = 'application/octet-stream'
    } elseif ($null -ne $Body) {
        $options.Body = $Body | ConvertTo-Json -Depth 5 -Compress
        $options.ContentType = 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod @options
}

function Publish-BridgeRelease {
    param([string]$ReleaseVersion, [string]$Setup, [string]$Commit)
    $file = Get-Item -LiteralPath $Setup
    if ($file.Name -cne "GEXMaestroBridgeSetup-$ReleaseVersion-x64.exe" -or $file.Length -eq 0) {
        throw 'Installer filename must match the released version and contain a nonempty setup program.'
    }
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $release = $null
    try {
        $release = Invoke-GitHubRequest GET "releases/tags/$ReleaseVersion"
    } catch {
        if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
    }
    if ($null -eq $release) {
        $release = Invoke-GitHubRequest POST 'releases' @{
            tag_name = $ReleaseVersion
            target_commitish = $Commit
            name = "GEX Maestro Bridge $ReleaseVersion"
            body = "Windows x64 setup for GEX Maestro Bridge $ReleaseVersion, including HIDMaestro and the required runtime."
            draft = $true
            prerelease = $false
        }
    }
    if ($release.tag_name -cne $ReleaseVersion -or $release.prerelease) {
        throw 'The version tag must identify a stable release.'
    }
    $assets = @()
    $page = 1
    do {
        $batch = @(Invoke-GitHubRequest GET "releases/$($release.id)/assets?per_page=100&page=$page")
        $assets += $batch
        $page++
    } while ($batch.Count -eq 100)
    $existing = @($assets | Where-Object { $_.name -ieq $file.Name })
    if ($existing.Count) {
        if ($existing.Count -ne 1 -or $existing[0].digest -ine $digest -or $existing[0].size -ne $file.Length -or $existing[0].state -ne 'uploaded') {
            throw 'A different or incomplete setup already exists for this version. Increment Version before publishing a changed installer.'
        }
        Write-Host 'This exact installer is already uploaded.'
    } else {
        if ($release.immutable) { throw 'The release is immutable and cannot accept an installer.' }
        $assetPath = "releases/$($release.id)/assets?name=" + [Uri]::EscapeDataString($file.Name)
        $uploaded = Invoke-GitHubRequest POST $assetPath -Asset $file.FullName
        if ($uploaded.state -ne 'uploaded' -or $uploaded.size -ne $file.Length -or ($uploaded.digest -and $uploaded.digest -ine $digest)) {
            throw 'GitHub did not confirm the complete installer upload. The release remains a draft if newly created.'
        }
    }
    if ($release.draft) {
        Invoke-GitHubRequest PATCH "releases/$($release.id)" @{draft = $false; prerelease = $false; make_latest = 'legacy'} | Out-Null
    }
    Write-Host "Published bridge $ReleaseVersion`: https://github.com/$script:Repository/releases/tag/$ReleaseVersion"
}

# Expose release orchestration to tests without credentials or network access.
if ($MyInvocation.InvocationName -eq '.') { return }
if (-not (Test-Path -LiteralPath $Installer)) { throw "Installer not found: $Installer" }
$commit = (& git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40,64}$') { throw 'Unable to determine the bridge source commit.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$script:GitHubHeaders = @{
    Authorization = 'Bearer ' + (Get-GitHubToken)
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'GEXMaestroBridge-Deploy'
}
Publish-BridgeRelease $Version $Installer $commit
