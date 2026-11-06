# Offline orchestration checks: no credentials, uploads, tags or releases are created.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'publish-release.ps1') -Version 0.1.0 -Installer unused
$script:Checks = 0
function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:Checks++
}
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $caught = $false
    try { & $Action } catch { $caught = $true }
    Assert-True $caught $Message
}
function Reset-Scenario {
    $script:Calls = New-Object 'Collections.Generic.List[object]'
    $script:RemoteRelease = $null
    $script:RemoteAssets = @()
    $script:UploadFails = $false
    $script:BadDigest = $false
    $script:LookupFails = $false
}
function Invoke-GitHubRequest {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Asset = '')
    $script:Calls.Add([pscustomobject]@{Method = $Method; Path = $Path; Body = $Body; Asset = $Asset})
    if ($Method -eq 'GET' -and $Path -eq 'releases/tags/0.1.0') {
        if ($script:LookupFails) { throw 'Authentication failure' }
        if ($null -eq $script:RemoteRelease) {
            $error404 = New-Object Exception 'Not found'
            Add-Member -InputObject $error404 -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{StatusCode = 404})
            throw $error404
        }
        return $script:RemoteRelease
    }
    if ($Method -eq 'POST' -and $Path -eq 'releases') {
        $script:RemoteRelease = [pscustomobject]@{id = 42; tag_name = $Body.tag_name; draft = $true; prerelease = $false; immutable = $false}
        return $script:RemoteRelease
    }
    if ($Method -eq 'GET' -and $Path -like 'releases/42/assets?*') {
        $page = [int]($Path -replace '^.*page=(\d+)$', '$1')
        return @($script:RemoteAssets | Select-Object -Skip (($page - 1) * 100) -First 100)
    }
    if ($Method -eq 'POST' -and $Asset) {
        if ($script:UploadFails) { throw 'Upload failed' }
        $digest = if ($script:BadDigest) { 'sha256:bad' } else { $script:ExpectedDigest }
        return [pscustomobject]@{state = 'uploaded'; size = $script:SetupSize; digest = $digest}
    }
    if ($Method -eq 'PATCH') { return [pscustomobject]@{draft = $false} }
    throw "Unexpected request: $Method $Path"
}

$stage = Join-Path ([IO.Path]::GetTempPath()) ('gex-bridge-release-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$setup = Join-Path $stage 'GEXMaestroBridgeSetup-0.1.0-x64.exe'
[IO.File]::WriteAllBytes($setup, (New-Object byte[] 2048))
$script:SetupSize = (Get-Item -LiteralPath $setup).Length
$script:ExpectedDigest = 'sha256:' + (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$commit = 'a' * 40
try {
    Reset-Scenario
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True ($script:Calls.Count -eq 5) 'Create, list, upload and publish after the version lookup.'
    $created = $script:Calls[1].Body
    Assert-True ($created.tag_name -ceq '0.1.0') 'Use the exact released version as the tag.'
    Assert-True ($created.target_commitish -eq $commit) 'Tag the source commit.'
    Assert-True ($created.draft -and -not $created.prerelease) 'Stage the asset on a stable draft.'
    Assert-True ($script:Calls[3].Asset -eq $setup) 'Upload the installer to the release.'
    Assert-True (-not $script:Calls[4].Body.draft) 'Publish only after successful upload.'
    Assert-True ($script:Calls[4].Body.make_latest -eq 'legacy') 'Support Latest resolution by semantic version.'

    foreach ($failure in @('UploadFails', 'BadDigest', 'LookupFails')) {
        Reset-Scenario
        Set-Variable -Scope Script -Name $failure -Value $true
        Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } "Propagate $failure."
        Assert-True (@($script:Calls | Where-Object Method -eq PATCH).Count -eq 0) 'Do not publish an incomplete release.'
    }

    Reset-Scenario
    $script:RemoteRelease = [pscustomobject]@{id = 42; tag_name = '0.1.0'; draft = $false; prerelease = $false; immutable = $false}
    $script:RemoteAssets = @([pscustomobject]@{name = 'GEXMaestroBridgeSetup-0.1.0-x64.exe'; size = $script:SetupSize; digest = $script:ExpectedDigest; state = 'uploaded'})
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True (@($script:Calls | Where-Object Method -ne GET).Count -eq 0) 'An identical released asset requires no writes.'
    $script:RemoteAssets[0].digest = 'sha256:different'
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'Do not overwrite different binaries for an existing version.'
    Assert-True (@($script:Calls | Where-Object Method -ne GET).Count -eq 0) 'Reject conflicting assets before mutation.'

    $script:RemoteRelease.prerelease = $true
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'Reject prereleases that Latest cannot discover.'
    $script:RemoteRelease.prerelease = $false
    $script:RemoteRelease.tag_name = 'v0.1.0'
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'Reject a tag that does not match the release version.'
    $script:RemoteRelease.tag_name = '0.1.0'
    $script:RemoteRelease.immutable = $true
    $script:RemoteAssets = @()
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'Reject uploads to immutable releases.'

    Reset-Scenario
    $script:RemoteRelease = [pscustomobject]@{id = 42; tag_name = '0.1.0'; draft = $false; prerelease = $false; immutable = $false}
    $script:RemoteAssets = @(1..100 | ForEach-Object { [pscustomobject]@{name = "other-$_.zip"} })
    $script:RemoteAssets += [pscustomobject]@{name = 'GEXMaestroBridgeSetup-0.1.0-x64.exe'; size = $script:SetupSize; digest = $script:ExpectedDigest; state = 'uploaded'}
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True ($script:Calls.Count -eq 3) 'Find existing assets beyond the first page.'
    Assert-True (@($script:Calls | Where-Object Method -ne GET).Count -eq 0) 'Pagination must not upload a duplicate.'
    Assert-Throws { Publish-BridgeRelease 0.2.0 $setup $commit } 'Reject an installer filename for another version.'
} finally {
    Remove-Item -LiteralPath $setup -Force
    Remove-Item -LiteralPath $stage -Force
}
Write-Host "Passed $script:Checks release checks. No GitHub requests were made."
