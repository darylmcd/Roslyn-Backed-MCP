<#
.SYNOPSIS
Pure CI validation-topology decision, extracted from .github/workflows/ci.yml's `route` job.

.DESCRIPTION
Given the triggering event kind and the already-fetched pull-request file-listing pages, decides
which runner-leg matrix a run should use and why. This script performs no GitHub API access of
its own -- the `route` job's "Decide validation topology" step fetches the
`gh api .../pulls/<n>/files` pages and hands them here as -ChangedFilesJson, so the exact same
decision logic that runs in CI can also run directly, or against a captured repro payload, from
tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs.

Fail-closed policy for pull_request events: when the changed-file enumeration cannot be trusted --
the caller signals an outright failure via -EnumerationFailed, or the enumerated raw file-record
count does not match GitHub's reported `pull_request.changed_files` count, or the enumerated count
reaches GitHub's files-API pagination ceiling (3000) -- the decision routes full ("code PR")
validation rather than the cheaper docs-only path. A partial or unverifiable file listing must
never be trusted to justify skipping test coverage.

.OUTPUTS
A single-line compressed JSON object on stdout: { "docs_only": bool, "runner_matrix": [...],
"reason": "..." }. The `runner_matrix` shape matches the per-leg fields the `validate` job's
matrix strategy consumes (name, runs_on, artifact_owner, timeout_minutes, test_shard_index,
test_shard_count) -- unchanged from the pre-extraction inline script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('pull_request', 'workflow_dispatch', 'schedule')]
    [string] $EventName,

    # JSON array of pages, each page itself a JSON array of GitHub pull-request file records
    # ({"filename": "...", "previous_filename": "..."} or without previous_filename). This is the
    # exact shape `gh api --paginate --slurp ".../pulls/<n>/files"` returns. Ignored for
    # non-pull_request events.
    [string] $ChangedFilesJson = '[]',

    # Alternative to -ChangedFilesJson for payloads too large to pass as a single command-line
    # argument (e.g. a real large PR, or a table test near the 3000-record pagination ceiling):
    # a path to a file containing the same JSON. Takes precedence over -ChangedFilesJson when set.
    [string] $ChangedFilesJsonPath,

    # GitHub's own `pull_request.changed_files` count. Required for pull_request events unless
    # -EnumerationFailed is set.
    [Nullable[int]] $ReportedChangedFileCount,

    # Set when the caller's own pull-request file-listing API call failed outright (nonzero exit,
    # transport error, malformed payload) rather than returning an incomplete/mismatched count.
    # Routes full validation, fail-closed, the same as a detected count mismatch, but with a
    # distinct reason so the two fail-closed causes remain distinguishable.
    [switch] $EnumerationFailed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-CiTopologyLeg {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)]
        [ValidateSet('ubuntu-latest', 'windows-latest')]
        [string] $RunsOn,
        [Parameter(Mandatory)][bool] $ArtifactOwner,
        [Parameter(Mandatory)][int] $TimeoutMinutes,
        [Parameter(Mandatory)][int] $TestShardIndex,
        [Parameter(Mandatory)][int] $TestShardCount
    )

    [ordered]@{
        name              = $Name
        runs_on           = $RunsOn
        artifact_owner    = $ArtifactOwner
        timeout_minutes   = $TimeoutMinutes
        test_shard_index  = $TestShardIndex
        test_shard_count  = $TestShardCount
    }
}

$codePullRequest = @(
    New-CiTopologyLeg -Name 'windows-hosted-1-of-4' -RunsOn 'windows-latest' -ArtifactOwner $false -TimeoutMinutes 45 -TestShardIndex 0 -TestShardCount 4
    New-CiTopologyLeg -Name 'windows-hosted-2-of-4' -RunsOn 'windows-latest' -ArtifactOwner $false -TimeoutMinutes 45 -TestShardIndex 1 -TestShardCount 4
    New-CiTopologyLeg -Name 'windows-hosted-3-of-4' -RunsOn 'windows-latest' -ArtifactOwner $false -TimeoutMinutes 45 -TestShardIndex 2 -TestShardCount 4
    New-CiTopologyLeg -Name 'windows-hosted-4-of-4' -RunsOn 'windows-latest' -ArtifactOwner $false -TimeoutMinutes 45 -TestShardIndex 3 -TestShardCount 4
    New-CiTopologyLeg -Name 'linux-1-of-2' -RunsOn 'ubuntu-latest' -ArtifactOwner $true -TimeoutMinutes 30 -TestShardIndex 0 -TestShardCount 2
    New-CiTopologyLeg -Name 'linux-2-of-2' -RunsOn 'ubuntu-latest' -ArtifactOwner $false -TimeoutMinutes 30 -TestShardIndex 1 -TestShardCount 2
)
$docsOnlyPullRequest = @(
    New-CiTopologyLeg -Name 'docs-linux-1-of-2' -RunsOn 'ubuntu-latest' -ArtifactOwner $true -TimeoutMinutes 30 -TestShardIndex 0 -TestShardCount 2
    New-CiTopologyLeg -Name 'docs-linux-2-of-2' -RunsOn 'ubuntu-latest' -ArtifactOwner $false -TimeoutMinutes 30 -TestShardIndex 1 -TestShardCount 2
)
$scheduledValidation = @(
    New-CiTopologyLeg -Name 'linux-full' -RunsOn 'ubuntu-latest' -ArtifactOwner $true -TimeoutMinutes 45 -TestShardIndex 0 -TestShardCount 1
)

# Documentation-shaped paths that would otherwise route docs-only, minus the behavior-bearing
# subset (executable prompts/skills/agents and the changelog) that must always force full
# validation even though their extension matches the documentation pattern.
$documentationPattern = '^(.*\.md|ai_docs/.*\.json)$'
$behaviorBearingMarkdownPattern =
    '(^CHANGELOG\.md$|^(skills|\.claude/skills|agents|\.claude/agents|\.github/prompts)/)'

function Get-OptionalPropertyValue {
    # Set-StrictMode -Version Latest turns a direct '.previous_filename' access into a terminating
    # error when a JSON-deserialized record simply omits the key -- which is exactly GitHub's real
    # files-API shape for a non-renamed file (previous_filename is present only on a rename). This
    # indirection reads the property only when it exists, returning $null otherwise.
    param(
        [Parameter(Mandatory)][object] $InputObject,
        [Parameter(Mandatory)][string] $Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-EnumeratedChangedPaths {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Pages
    )

    $enumeratedFileCount = 0
    $changedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($page in @($Pages)) {
        foreach ($file in @($page)) {
            $enumeratedFileCount++
            $previousFilename = Get-OptionalPropertyValue -InputObject $file -Name 'previous_filename'
            foreach ($path in @($file.filename, $previousFilename)) {
                if (-not [string]::IsNullOrWhiteSpace($path)) {
                    [void]$changedSet.Add($path)
                }
            }
        }
    }

    [pscustomobject]@{
        EnumeratedFileCount = $enumeratedFileCount
        ChangedPaths        = @($changedSet)
    }
}

function Resolve-CiTopologyDecision {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('pull_request', 'workflow_dispatch', 'schedule')]
        [string] $EventName,
        [string] $ChangedFilesJson = '[]',
        [Nullable[int]] $ReportedChangedFileCount,
        [switch] $EnumerationFailed
    )

    if ($EventName -ne 'pull_request') {
        return [ordered]@{
            docs_only     = $false
            runner_matrix = @($scheduledValidation)
            reason        = 'Dispatch/schedule: one unsharded Linux coverage leg.'
        }
    }

    if ($EnumerationFailed) {
        return [ordered]@{
            docs_only     = $false
            runner_matrix = @($codePullRequest)
            reason        = 'Pull-request file listing could not be verified (API failure); routing full validation.'
        }
    }

    if ($null -eq $ReportedChangedFileCount) {
        throw 'ReportedChangedFileCount is required for pull_request events unless -EnumerationFailed is set.'
    }

    $pages = @(
        if ([string]::IsNullOrWhiteSpace($ChangedFilesJson)) {
            @()
        }
        else {
            ConvertFrom-Json -InputObject $ChangedFilesJson -ErrorAction Stop
        }
    )
    $enumeration = Get-EnumeratedChangedPaths -Pages $pages
    $enumeratedFileCount = $enumeration.EnumeratedFileCount
    $changed = @($enumeration.ChangedPaths)

    $docsOnly = $false
    if ($enumeratedFileCount -ge 3000 -or $enumeratedFileCount -ne $ReportedChangedFileCount) {
        # Write-Warning renders through the host and, under `pwsh -File` with stdout redirected,
        # lands on stdout (with ANSI escapes) ahead of this script's sole intended stdout output --
        # the compressed decision JSON. Write directly to stderr so stdout stays parseable.
        [Console]::Error.WriteLine(
            "WARNING: GitHub reported $ReportedChangedFileCount changed files but the files API " +
            "returned $enumeratedFileCount; route full validation because the API result may be " +
            "capped or incomplete.")
    }
    else {
        $nonDocs = @($changed | Where-Object {
            $_ -notmatch $documentationPattern -or $_ -match $behaviorBearingMarkdownPattern
        })
        $docsOnly = $changed.Count -gt 0 -and $nonDocs.Count -eq 0
    }

    if ($docsOnly) {
        return [ordered]@{
            docs_only     = $true
            runner_matrix = @($docsOnlyPullRequest)
            reason        = 'Policy-only docs PR: two hosted Linux test shards.'
        }
    }

    return [ordered]@{
        docs_only     = $false
        runner_matrix = @($codePullRequest)
        reason        = 'Code PR: four hosted Windows and two hosted Linux shards.'
    }
}

$effectiveChangedFilesJson = $ChangedFilesJson
if (-not [string]::IsNullOrWhiteSpace($ChangedFilesJsonPath)) {
    $effectiveChangedFilesJson = Get-Content -Raw -LiteralPath $ChangedFilesJsonPath
}

$decision = Resolve-CiTopologyDecision `
    -EventName $EventName `
    -ChangedFilesJson $effectiveChangedFilesJson `
    -ReportedChangedFileCount $ReportedChangedFileCount `
    -EnumerationFailed:$EnumerationFailed.IsPresent

$decision | ConvertTo-Json -Compress -Depth 8
