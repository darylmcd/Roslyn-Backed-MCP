<#
.SYNOPSIS
Runs a checksum-pinned, repository-owned actionlint against .github/workflows/*.yml.

.DESCRIPTION
No developer-global actionlint install is required. The pinned version and every RID's
archive/binary SHA-256 are declared below; bump all of them together when upgrading, the
same discipline eng/verify-release.ps1 applies to its publish-output hash manifest.

Archive hashes come from actionlint's published `<version>_checksums.txt`; binary hashes are
computed once at pin time by extracting each archive and hashing the resulting executable, so
a pre-staged binary (ROSLYNMCP_ACTIONLINT_PATH) can be verified without re-deriving the
archive it came from.

Resolution order:
  1. ROSLYNMCP_ACTIONLINT_PATH, if set -- verified against the pinned BINARY hash.
  2. A cached binary under artifacts/tools/actionlint/<version>/ -- re-verified against the
     pinned BINARY hash on every run (a corrupted or tampered cache entry fails closed
     instead of running silently). Pure local hashing: never touches the network.
  3. Download the pinned archive, hash it BEFORE extracting, fail closed on mismatch or on
     any network failure -- never falls back to an unpinned download.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(DontShow)]
    [switch]$FailChmodForTest,
    [Parameter(DontShow)]
    [ValidateSet('windows', 'macos', 'linux', 'unsupported')]
    [string]$PlatformForTest,
    [Parameter(DontShow)]
    [ValidateSet('x64', 'arm64')]
    [string]$ArchitectureForTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PinnedVersion = '1.7.12'
$UnsupportedPlatformDiagnostic =
    'verify-actionlint: unsupported platform (neither Windows, macOS, nor Linux detected).'
$PinnedArchiveHashes = [ordered]@{
    'win-x64'     = [ordered]@{
        Asset         = "actionlint_${PinnedVersion}_windows_amd64.zip"
        ArchiveSha256 = '6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9'
        BinarySha256  = '54ca21be3de4c7cfa26914aa8b61bd76bf573ef3caac5f80d110558cdf241718'
        BinaryName    = 'actionlint.exe'
    }
    'linux-x64'   = [ordered]@{
        Asset         = "actionlint_${PinnedVersion}_linux_amd64.tar.gz"
        ArchiveSha256 = '8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8'
        BinarySha256  = 'c872d6db8c6bf83a8eaa704fc93999f027d55dffbc63b8a6abdccb47df5f4cd4'
        BinaryName    = 'actionlint'
    }
    'linux-arm64' = [ordered]@{
        Asset         = "actionlint_${PinnedVersion}_linux_arm64.tar.gz"
        ArchiveSha256 = '325e971b6ba9bfa504672e29be93c24981eeb1c07576d730e9f7c8805afff0c6'
        BinarySha256  = 'ac0323433c2853ec3fb978c611430c5b3dc5d43c58d1a1ec031b00ab572beb60'
        BinaryName    = 'actionlint'
    }
    'osx-arm64'   = [ordered]@{
        Asset         = "actionlint_${PinnedVersion}_darwin_arm64.tar.gz"
        ArchiveSha256 = 'aba9ced2dee8d27fecca3dc7feb1a7f9a52caefa1eb46f3271ea66b6e0e6953f'
        BinarySha256  = '8db11704dc296f096216db4db65d86cd7f0ebfdf4c38453a1da276b137b88388'
        BinaryName    = 'actionlint'
    }
}

function Stop-WithDiagnostic {
    param(
        [Parameter(Mandatory)][string]$Diagnostic,
        [int]$ExitCode = 1
    )

    [Console]::Error.WriteLine($Diagnostic)
    exit $ExitCode
}

function Get-CurrentRid {
    param(
        [string]$PlatformForTest,
        [string]$ArchitectureForTest
    )

    if ($PSBoundParameters.ContainsKey('PlatformForTest')) {
        $platform = $PlatformForTest
    }
    elseif ($IsWindows) {
        $platform = 'windows'
    }
    elseif ($IsMacOS) {
        $platform = 'macos'
    }
    elseif ($IsLinux) {
        $platform = 'linux'
    }
    else {
        $platform = 'unsupported'
    }

    if ($platform -eq 'unsupported') {
        Stop-WithDiagnostic -Diagnostic $UnsupportedPlatformDiagnostic
    }

    $architecture = if ($PSBoundParameters.ContainsKey('ArchitectureForTest')) {
        $ArchitectureForTest
    }
    else {
        ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture).ToString().ToLowerInvariant()
    }
    $ridPlatform = switch ($platform) {
        'windows' { 'win' }
        'macos' { 'osx' }
        'linux' { 'linux' }
    }

    return "$ridPlatform-$architecture"
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Set-ActionlintExecutablePermission {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(DontShow)][switch]$FailForTest
    )
    if ($FailForTest) {
        & (Get-Process -Id $PID).Path -NoProfile -NonInteractive -Command 'exit 1'
    }
    else {
        & chmod +x $Path 2>$null
    }
    $chmodExitCode = $LASTEXITCODE
    if ($chmodExitCode -ne 0) {
        Stop-WithDiagnostic -ExitCode $chmodExitCode -Diagnostic (
            "verify-actionlint: failed to mark actionlint executable (chmod exit code $chmodExitCode).")
    }
}

if ($FailChmodForTest) {
    Set-ActionlintExecutablePermission -Path 'test-only' -FailForTest
}

$ridArguments = @{}
if ($PSBoundParameters.ContainsKey('PlatformForTest')) {
    $ridArguments['PlatformForTest'] = $PlatformForTest
}
if ($PSBoundParameters.ContainsKey('ArchitectureForTest')) {
    $ridArguments['ArchitectureForTest'] = $ArchitectureForTest
}
$rid = Get-CurrentRid @ridArguments
if (-not $PinnedArchiveHashes.Contains($rid)) {
    Stop-WithDiagnostic -Diagnostic (
        "verify-actionlint: no pinned actionlint archive/hash recorded for RID '$rid'.")
}
$pin = $PinnedArchiveHashes[$rid]

$toolRoot = Join-Path $RepoRoot "artifacts/tools/actionlint/$PinnedVersion"
$binaryPath = Join-Path $toolRoot $pin.BinaryName

if ($env:ROSLYNMCP_ACTIONLINT_PATH) {
    $overridePath = $env:ROSLYNMCP_ACTIONLINT_PATH
    if (-not (Test-Path -LiteralPath $overridePath -PathType Leaf)) {
        throw "verify-actionlint: ROSLYNMCP_ACTIONLINT_PATH='$overridePath' does not exist."
    }
    $overrideHash = Get-FileSha256 -Path $overridePath
    if ($overrideHash -ne $pin.BinarySha256) {
        throw "verify-actionlint: ROSLYNMCP_ACTIONLINT_PATH='$overridePath' hash mismatch (expected $($pin.BinarySha256), got $overrideHash) -- refusing to run an unverified binary."
    }
    $binaryPath = $overridePath
}
elseif (Test-Path -LiteralPath $binaryPath -PathType Leaf) {
    $cachedHash = Get-FileSha256 -Path $binaryPath
    if ($cachedHash -ne $pin.BinarySha256) {
        throw "verify-actionlint: cached binary at '$binaryPath' hash mismatch (expected $($pin.BinarySha256), got $cachedHash) -- delete $toolRoot and re-run to re-download."
    }
}
else {
    $downloadUrl = "https://github.com/rhysd/actionlint/releases/download/v$PinnedVersion/$($pin.Asset)"
    New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
    $archivePath = Join-Path $toolRoot $pin.Asset
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    }
    catch {
        throw "verify-actionlint: no cached actionlint binary at '$binaryPath' and the pinned archive could not be downloaded from '$downloadUrl' (expected SHA-256 $($pin.ArchiveSha256)): $($_.Exception.Message)"
    }
    $archiveHash = Get-FileSha256 -Path $archivePath
    if ($archiveHash -ne $pin.ArchiveSha256) {
        Remove-Item -LiteralPath $archivePath -Force
        throw "verify-actionlint: downloaded archive '$downloadUrl' hash mismatch (expected $($pin.ArchiveSha256), got $archiveHash) -- refusing to extract an unverified archive."
    }
    if ($rid -like 'win-*') {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $toolRoot -Force
    }
    else {
        & tar -xzf $archivePath -C $toolRoot
        if ($LASTEXITCODE -ne 0) { throw "verify-actionlint: 'tar' extraction of '$archivePath' failed with exit code $LASTEXITCODE." }
    }
    Remove-Item -LiteralPath $archivePath -Force
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
        throw "verify-actionlint: extraction of '$($pin.Asset)' did not produce the expected binary at '$binaryPath'."
    }
    $extractedHash = Get-FileSha256 -Path $binaryPath
    if ($extractedHash -ne $pin.BinarySha256) {
        throw "verify-actionlint: extracted binary at '$binaryPath' hash mismatch (expected $($pin.BinarySha256), got $extractedHash) -- the pinned archive hash matched but its extracted contents did not."
    }
}

if ($rid -notlike 'win-*') {
    Set-ActionlintExecutablePermission -Path $binaryPath
}

$workflowDir = Join-Path $RepoRoot '.github/workflows'
$workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' -File)
if ($workflowFiles.Count -eq 0) {
    throw "verify-actionlint: no workflow files found under $workflowDir."
}

Write-Host "verify-actionlint: running pinned actionlint $PinnedVersion ($rid) against $($workflowFiles.Count) workflow file(s)."
& $binaryPath @($workflowFiles.FullName)
$actionlintExitCode = $LASTEXITCODE
if ($actionlintExitCode -ne 0) {
    throw "verify-actionlint: actionlint reported issue(s) (exit code $actionlintExitCode) -- see output above."
}
Write-Host 'verify-actionlint: no issues found.'
