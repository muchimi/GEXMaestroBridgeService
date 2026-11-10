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
    $script:MissingUploadId = $false
    $script:RenameFails = $false
    $script:NextAssetId = 900
}
function Set-ExistingInstaller {
    param([string]$Digest = 'sha256:different', [string]$State = 'uploaded', [bool]$Draft = $false)
    $script:RemoteRelease = [pscustomobject]@{id = 42; tag_name = '0.1.0'; draft = $Draft; prerelease = $false; immutable = $false}
    $script:RemoteAssets = @([pscustomobject]@{id = 101; name = 'GEXMaestroBridgeSetup-0.1.0-x64.exe'; size = $script:SetupSize; digest = $Digest; state = $State})
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
        $id = $script:NextAssetId
        $script:NextAssetId++
        if ($script:MissingUploadId) { $id = $null }
        $name = [Uri]::UnescapeDataString(($Path -split '\?name=', 2)[1])
        $uploaded = [pscustomobject]@{id = $id; name = $name; state = 'uploaded'; size = $script:SetupSize; digest = $digest}
        $script:RemoteAssets += $uploaded
        return $uploaded
    }
    if ($Method -eq 'DELETE' -and $Path -match '^releases/assets/(\d+)$') {
        $id = [long]$Matches[1]
        $script:RemoteAssets = @($script:RemoteAssets | Where-Object { $_.id -ne $id })
        return
    }
    if ($Method -eq 'PATCH' -and $Path -match '^releases/assets/(\d+)$') {
        if ($script:RenameFails) { throw 'Rename failed' }
        $id = [long]$Matches[1]
        $assetInfo = $script:RemoteAssets | Where-Object { $_.id -eq $id } | Select-Object -First 1
        if (-not $assetInfo) { throw 'Replacement upload not found.' }
        $assetInfo.name = $Body.name
        return $assetInfo
    }
    if ($Method -eq 'PATCH' -and $Path -eq 'releases/42') {
        $script:RemoteRelease.draft = $Body.draft
        return $script:RemoteRelease
    }
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
    Set-ExistingInstaller -Digest $script:ExpectedDigest
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True (@($script:Calls | Where-Object Method -ne GET).Count -eq 0) 'An identical released asset requires no writes.'
    $script:Calls.Clear()
    $script:RemoteAssets[0].digest = 'sha256:different'
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True ($script:Calls.Count -eq 5) 'Read, upload, remove the old installer, and rename the replacement.'
    Assert-True ($script:Calls[2].Method -eq 'POST' -and $script:Calls[2].Path -like '*assets?name=*.replacement-*') 'Upload under a temporary name before deleting the published file.'
    Assert-True ($script:Calls[3].Method -eq 'DELETE' -and $script:Calls[3].Path -eq 'releases/assets/101') 'Remove the prior matching installer.'
    Assert-True ($script:Calls[4].Method -eq 'PATCH' -and $script:Calls[4].Body.name -ceq 'GEXMaestroBridgeSetup-0.1.0-x64.exe') 'Restore the expected installer filename.'
    Assert-True ($script:RemoteAssets.Count -eq 1 -and $script:RemoteAssets[0].digest -ceq $script:ExpectedDigest) 'The published installer now matches the local installer.'

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

    foreach ($state in @('starter', 'uploaded')) {
        Reset-Scenario
        Set-ExistingInstaller -Digest '' -State $state
        Publish-BridgeRelease 0.1.0 $setup $commit
        Assert-True ($script:RemoteAssets.Count -eq 1 -and $script:RemoteAssets[0].state -eq 'uploaded') "Replace an incomplete or checksum-less $state asset."
    }

    foreach ($failure in @('UploadFails', 'BadDigest', 'MissingUploadId')) {
        Reset-Scenario
        Set-ExistingInstaller
        Set-Variable -Scope Script -Name $failure -Value $true
        Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } "Report replacement $failure."
        Assert-True (@($script:Calls | Where-Object Method -eq DELETE).Count -eq 0) 'Keep the prior installer until the replacement upload is verified.'
        Assert-True (@($script:RemoteAssets | Where-Object id -eq 101).Count -eq 1) 'The prior published file survives an upload failure.'
    }

    Reset-Scenario
    Set-ExistingInstaller
    $script:RemoteRelease.immutable = $true
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'GitHub immutable releases cannot be overwritten.'
    Assert-True (@($script:Calls | Where-Object Method -ne GET).Count -eq 0) 'Do not mutate an immutable release.'

    Reset-Scenario
    Set-ExistingInstaller
    $script:RemoteAssets += [pscustomobject]@{id = 102; name = 'GEXMaestroBridgeSetup-0.1.0-x64.exe'; size = 0; state = 'starter'}
    $script:RemoteAssets += [pscustomobject]@{id = 103; name = 'release-notes.txt'}
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True (@($script:Calls | Where-Object Method -eq DELETE).Count -eq 2) 'Replace all old assets matching this installer filename.'
    Assert-True (@($script:RemoteAssets | Where-Object id -eq 103).Count -eq 1) 'Retain unrelated release assets.'
    Assert-True (@($script:RemoteAssets | Where-Object name -eq 'GEXMaestroBridgeSetup-0.1.0-x64.exe').Count -eq 1) 'Leave one canonical installer after replacing duplicate matches.'

    Reset-Scenario
    Set-ExistingInstaller -Draft $true
    Publish-BridgeRelease 0.1.0 $setup $commit
    Assert-True ($script:Calls[-1].Path -eq 'releases/42' -and $script:Calls[-1].Body.draft -eq $false) 'Publish a draft only after the replacement is renamed successfully.'

    Reset-Scenario
    Set-ExistingInstaller -Draft $true
    $script:RenameFails = $true
    Assert-Throws { Publish-BridgeRelease 0.1.0 $setup $commit } 'Report a replacement rename failure.'
    Assert-True $script:RemoteRelease.draft 'A rename failure must not publish a draft.'
    Assert-True (@($script:RemoteAssets | Where-Object { $_.name -like '*.replacement-*' -and $_.digest -eq $script:ExpectedDigest }).Count -eq 1) 'Keep the verified replacement available for recovery if renaming fails.'
} finally {
    Remove-Item -LiteralPath $setup -Force
    Remove-Item -LiteralPath $stage -Force
}
Write-Host "Passed $script:Checks release checks. No GitHub requests were made."
