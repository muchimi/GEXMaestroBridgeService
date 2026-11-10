param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '',
    [string]$Installer = ''
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
    $identical = $existing.Count -eq 1 -and $existing[0].digest -ieq $digest -and
        $existing[0].size -eq $file.Length -and $existing[0].state -eq 'uploaded'
    if ($identical) {
        Write-Host 'This exact installer is already uploaded.'
    } else {
        if ($release.immutable) { throw 'GitHub marks this release as immutable; its installer cannot be added or replaced.' }
        foreach ($asset in $existing) {
            if (-not $asset.id -or [string]$asset.id -notmatch '^\d+$') { throw 'An existing installer has no valid GitHub asset ID.' }
        }
        $uploadName = $file.Name
        if ($existing.Count) {
            # Verify the replacement before removing the currently published file.
            $uploadName += '.replacement-' + [guid]::NewGuid().ToString('N')
        }
        $assetPath = "releases/$($release.id)/assets?name=" + [Uri]::EscapeDataString($uploadName)
        $uploaded = Invoke-GitHubRequest POST $assetPath -Asset $file.FullName
        if ($uploaded.state -ne 'uploaded' -or $uploaded.size -ne $file.Length -or ($uploaded.digest -and $uploaded.digest -ine $digest)) {
            throw 'GitHub did not confirm the complete installer upload. The release remains a draft if newly created.'
        }
        if ($existing.Count) {
            if (-not $uploaded.id -or [string]$uploaded.id -notmatch '^\d+$') { throw 'The replacement upload has no valid GitHub asset ID; the prior installer was retained.' }
            try {
                foreach ($asset in $existing) {
                    Invoke-GitHubRequest DELETE "releases/assets/$($asset.id)" | Out-Null
                }
                $renamed = Invoke-GitHubRequest PATCH "releases/assets/$($uploaded.id)" @{name = $file.Name}
                if ($renamed.name -cne $file.Name -or $renamed.state -ne 'uploaded' -or $renamed.size -ne $file.Length -or
                    ($renamed.digest -and $renamed.digest -ine $digest)) {
                    throw 'GitHub did not confirm the replacement installer name and contents.'
                }
            } catch {
                throw "Switching the published installer failed. The verified replacement was uploaded as '$uploadName'. $($_.Exception.Message)"
            }
            Write-Host "Replaced the previously uploaded installer for bridge $ReleaseVersion."
        }
    }
    if ($release.draft) {
        Invoke-GitHubRequest PATCH "releases/$($release.id)" @{draft = $false; prerelease = $false; make_latest = 'legacy'} | Out-Null
    }
    Write-Host "Published bridge $ReleaseVersion`: https://github.com/$script:Repository/releases/tag/$ReleaseVersion"
}

function Resolve-ReleaseVersion {
    param([string]$RequestedVersion)
    if ($RequestedVersion) { return $RequestedVersion }
    $root = Split-Path -Parent $PSScriptRoot
    $project = Join-Path $root 'src\MaestroOscService\GEXMaestroBridgeService.csproj'
    $detected = (& dotnet msbuild $project -nologo -getProperty:Version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0 -or $detected -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Unable to determine release version from the bridge project.'
    }
    return $detected
}

function Resolve-InstallerPath {
    param([string]$ReleaseVersion, [string]$RequestedInstaller)
    $root = Split-Path -Parent $PSScriptRoot
    if ($RequestedInstaller) {
        if (-not [IO.Path]::IsPathRooted($RequestedInstaller)) { $RequestedInstaller = Join-Path $root $RequestedInstaller }
        if (-not (Test-Path -LiteralPath $RequestedInstaller)) { throw "Installer not found: $RequestedInstaller" }
        return (Resolve-Path -LiteralPath $RequestedInstaller).Path
    }

    $expectedName = "GEXMaestroBridgeSetup-$ReleaseVersion-x64.exe"
    $defaultPath = Join-Path $root (Join-Path 'artifacts\installer' $expectedName)
    if (-not (Test-Path -LiteralPath $defaultPath)) {
        Write-Host "Installer $expectedName not found. Building installer for version $ReleaseVersion..."
        & (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $ReleaseVersion
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    }
    if (-not (Test-Path -LiteralPath $defaultPath)) {
        throw "Installer build completed but expected file is missing: $defaultPath"
    }
    return (Resolve-Path -LiteralPath $defaultPath).Path
}

# Expose release orchestration to tests without credentials or network access.
if ($MyInvocation.InvocationName -eq '.') { return }
$Version = Resolve-ReleaseVersion $Version
$Installer = Resolve-InstallerPath $Version $Installer
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
