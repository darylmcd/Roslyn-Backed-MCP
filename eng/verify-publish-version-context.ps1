#requires -Version 7.0
<#
.SYNOPSIS
    Validates that the release provenance (pushed tag / published GitHub release name)
    matches the canonical package version before the publish job builds or packs.

.DESCRIPTION
    verify-version-drift.ps1 proves the six in-repo version files agree with the
    canonical Directory.Build.props <Version>. Nothing proved that the *tag* that
    triggered the publish agrees with it. A mistag is silently survivable today:
    `dotnet nuget push --skip-duplicate` no-ops when the canonical version was
    already published, and the downstream publish-registry job derives its version
    from the tag, so it either polls nuget.org for a version that was never packed
    or records registry provenance for the wrong release.

    This gate closes that path. It runs as the first step of the publish job, before
    verify-release.ps1, so a mistag fails in seconds instead of after a full build.

    Event handling:
      push                — compares the normalized -RefName against the canonical version.
      release             — compares the normalized -ReleaseTag against the canonical version.
      workflow_dispatch   — no tag exists for a manual dry run; prints the canonical
                            version and performs no comparison.

    A tag is normalized by stripping a single leading 'v'. Anything that is not
    `v?MAJOR.MINOR.PATCH` (including an empty value on a tag/release event) fails.

    Exit codes:
      0  Provenance matches the canonical version, or the event carries no tag.
      1  Missing/malformed tag, or a tag that disagrees with the canonical version.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('push', 'release', 'workflow_dispatch')]
    [string]$EventName,

    [string]$RefName = '',

    [string]$ReleaseTag = '',

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

function Write-ProvenanceFailure {
    param([Parameter(Mandatory)][string[]]$Errors)

    Write-Host ''
    Write-Host 'RELEASE PROVENANCE MISMATCH:' -ForegroundColor Red
    foreach ($e in $Errors) {
        Write-Host "  - $e" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'The tag that triggers a publish must equal the canonical Directory.Build.props <Version>.' -ForegroundColor Yellow
    Write-Host 'Delete the bad tag/release, bump the version files, and re-tag. See docs/release-policy.md.' -ForegroundColor Yellow
}

try {
    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
}
catch {
    Write-Error "Repository root '$RepoRoot' could not be resolved."
    exit 1
}

$propsPath = Join-Path $resolvedRepoRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath)) {
    Write-Error "Could not find $propsPath"
    exit 1
}

[xml]$props = Get-Content -LiteralPath $propsPath
$canonical = "$($props.Project.PropertyGroup.Version)".Trim()
if (-not $canonical) {
    Write-Error "Could not read <Version> from $propsPath"
    exit 1
}

Write-Host "Canonical version (Directory.Build.props): $canonical"

if ($EventName -eq 'workflow_dispatch') {
    Write-Host "Event 'workflow_dispatch' carries no release tag; skipping provenance comparison." -ForegroundColor Yellow
    exit 0
}

if ($EventName -eq 'push') {
    $sourceLabel = 'github.ref_name'
    $rawTag = $RefName
}
else {
    $sourceLabel = 'github.event.release.tag_name'
    $rawTag = $ReleaseTag
}

$rawTag = "$rawTag".Trim()

if (-not $rawTag) {
    Write-ProvenanceFailure -Errors @(
        "$sourceLabel was empty on a '$EventName' event; the publish job cannot verify release provenance."
    )
    exit 1
}

if ($rawTag -notmatch '^v?\d+\.\d+\.\d+$') {
    Write-ProvenanceFailure -Errors @(
        "$sourceLabel '$rawTag' is not a vMAJOR.MINOR.PATCH release tag."
    )
    exit 1
}

$normalized = $rawTag -replace '^v', ''

if ($normalized -ne $canonical) {
    Write-ProvenanceFailure -Errors @(
        "$sourceLabel '$rawTag' resolves to version '$normalized', but Directory.Build.props declares '$canonical'."
    )
    exit 1
}

Write-Host "Release provenance OK: $sourceLabel '$rawTag' matches canonical version $canonical" -ForegroundColor Green
exit 0
