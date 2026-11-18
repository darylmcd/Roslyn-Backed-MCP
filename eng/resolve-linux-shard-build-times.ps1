<#
.SYNOPSIS
Collect the completed Linux Release build step durations for this Actions run.

.DESCRIPTION
Select the first successful build per shard across attempts. Every shard uses the same
complete vector, and a partial rerun retains the partition selected by its peers.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LegName,

    [Parameter(Mandatory)]
    [ValidateRange(2, 16)]
    [int]$ShardCount
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Only the sharded code route calibrates; the policy-docs leg is a single unsharded leg.
$leg = [regex]::Match($LegName, '^linux-(?<index>[1-9][0-9]*)-of-(?<count>[1-9][0-9]*)$')
if (-not $leg.Success -or [int]$leg.Groups['count'].Value -ne $ShardCount -or
    [int]$leg.Groups['index'].Value -gt $ShardCount -or
    $env:GITHUB_REPOSITORY -notmatch '^[\w.-]+/[\w.-]+$' -or
    $env:GITHUB_RUN_ID -notmatch '^[1-9][0-9]*$' -or
    $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9][0-9]*$') {
    throw 'Linux build calibration requires the current Actions run, attempt and shard identity.'
}
$currentAttempt = [int]$env:GITHUB_RUN_ATTEMPT
$attemptsEndpoint = "repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/attempts"
$buildStep = 'Measure Linux Release build'
$expectedJobs = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
for ($index = 1; $index -le $ShardCount; $index++) {
    $expectedJobs.Add("validate-leg (linux-$index-of-$ShardCount)", $index - 1)
}
$firstBuilds = [System.Collections.Generic.Dictionary[int, double]]::new()

function Add-CompletedBuilds {
    param([Parameter(Mandatory)][int]$Attempt)

    $response = & gh api --paginate --slurp "$attemptsEndpoint/$Attempt/jobs?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw "Could not read Actions jobs for calibration attempt $Attempt." }

    $pages = ConvertFrom-Json -InputObject ($response -join [Environment]::NewLine) -NoEnumerate
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($page in $pages) {
        foreach ($job in $page.jobs) {
            $index = 0
            if (-not $expectedJobs.TryGetValue($job.name, [ref]$index)) { continue }
            if ($job.run_id -ne [long]$env:GITHUB_RUN_ID -or -not $seen.Add($index)) {
                throw "Unexpected calibration job identity: $($job.name)"
            }
            if ($firstBuilds.ContainsKey($index)) { continue }

            $steps = @($job.steps | Where-Object { $_.name -ceq $buildStep })
            if ($steps.Count -gt 1) { throw "More than one calibration build in $($job.name)." }
            if ($steps.Count -eq 0 -or $steps[0].status -cne 'completed' -or
                $steps[0].conclusion -cne 'success') { continue }

            $step = $steps[0]
            $seconds = ([DateTimeOffset]$step.completed_at - [DateTimeOffset]$step.started_at).TotalSeconds
            if ($seconds -le 0) { throw "Invalid build duration for $($job.name)." }
            $firstBuilds.Add($index, $seconds)
        }
    }
}

# Previous attempts are frozen. Poll only the current attempt for unresolved shards.
for ($attempt = 1; $attempt -lt $currentAttempt -and $firstBuilds.Count -lt $ShardCount; $attempt++) {
    Add-CompletedBuilds -Attempt $attempt
}
$wait = [Diagnostics.Stopwatch]::StartNew()
while ($firstBuilds.Count -lt $ShardCount) {
    Add-CompletedBuilds -Attempt $currentAttempt
    if ($firstBuilds.Count -eq $ShardCount) { break }
    if ($wait.Elapsed.TotalMinutes -ge 3) {
        throw "Timed out waiting for Linux calibration builds ($($firstBuilds.Count)/$ShardCount)."
    }
    Start-Sleep -Seconds 5
}

[double[]]$seconds = @(for ($index = 0; $index -lt $ShardCount; $index++) { $firstBuilds[$index] })
ConvertTo-Json -InputObject $seconds -Compress
