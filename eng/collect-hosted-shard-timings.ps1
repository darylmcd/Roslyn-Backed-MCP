<#
.SYNOPSIS
Quantify hosted CI shard timing skew per hosted image from downloaded per-leg TRX evidence.

.DESCRIPTION
Offline, deterministic, and hermetic: this script never contacts GitHub. Download the
`test-results-<leg>` artifacts yourself, describe them in a JSON leg manifest, and point this
script at both.

The script answers one question: does repeated evidence justify replacing the deterministic
discovered-case weights in `eng/get-test-shard-plan.ps1` with duration weights?

Wall-time skew and summed TRX case duration are reported as SEPARATE metrics because they are
not interchangeable. Summed case duration on a hosted runner is a shared-machine measurement
whose same-leg run-to-run swing can exceed the between-leg spread; when it does, that metric
cannot drive a partition and the report says so.

Fail-closed rules (each throws rather than degrading):
  - fewer than MinimumSamples distinct runs for any hosted image
  - an entry missing leg, image, run id, wall time, or path (no untagged legs)
  - one leg claimed by two hosted images (images are never merged)
  - a run missing any leg of its own image's leg set
  - a duplicate run id + leg pair
  - a manifest path that escapes ResultsRoot, is absent, or holds no TRX

The Markdown report is written to stdout. Redirect it if you want it in a file; this script never
writes to a path you give it, so it can never append into its own downloaded TRX input.

.PARAMETER ResultsRoot
Directory holding the downloaded `test-results-<leg>` folders. Every manifest path resolves
beneath it.

.PARAMETER LegManifest
Path to a JSON array. Each element describes one leg observation:

  [
    {
      "runId": "33657245113",
      "leg": "windows-hosted-1-of-4",
      "image": "windows-latest",
      "wallTimeSeconds": 635,
      "path": "33657245113/test-results-windows-hosted-1-of-4"
    }
  ]

`wallTimeSeconds` is that leg job's own hosted wall clock. Never mix a local-machine timing
into a hosted image profile.

.PARAMETER MinimumSamples
Minimum distinct runs required per hosted image. Defaults to 5.

.EXAMPLE
pwsh -NoProfile -File ./eng/collect-hosted-shard-timings.ps1 -ResultsRoot ./downloads -LegManifest ./downloads/legs.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsRoot,

    [Parameter(Mandatory)]
    [string]$LegManifest,

    [ValidateRange(1, 1000)]
    [int]$MinimumSamples = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Format-Seconds {
    param([double]$Value)

    return $Value.ToString('0.0', $invariantCulture)
}

function Format-Ratio {
    param([double]$Value)

    if ([double]::IsInfinity($Value) -or [double]::IsNaN($Value)) {
        return 'n/a'
    }

    return $Value.ToString('0.00', $invariantCulture) + 'x'
}

function Get-RequiredText {
    param(
        [Parameter(Mandatory)][object]$Entry,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$Index
    )

    if ($Entry.PSObject.Properties.Name -notcontains $Name) {
        throw "Leg manifest entry $Index is untagged: '$Name' is missing."
    }

    $value = $Entry.$Name
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        throw "Leg manifest entry $Index is untagged: '$Name' is empty."
    }

    return ([string]$value).Trim()
}

function Get-RequiredSeconds {
    param(
        [Parameter(Mandatory)][object]$Entry,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$Index
    )

    if ($Entry.PSObject.Properties.Name -notcontains $Name) {
        throw "Leg manifest entry $Index is untagged: '$Name' is missing."
    }

    # A JSON number arrives already typed. Never round-trip it through the current culture's
    # string form, which would emit a decimal comma an invariant parse then rejects.
    $value = $Entry.$Name
    [double]$seconds = 0
    if ($value -is [double] -or $value -is [single] -or $value -is [decimal] -or
        $value -is [int] -or $value -is [long]) {
        $seconds = [double]$value
    }
    elseif ($null -ne $value -and
            -not [string]::IsNullOrWhiteSpace([string]$value) -and
            [double]::TryParse(
                ([string]$value).Trim(),
                [System.Globalization.NumberStyles]::Float,
                $invariantCulture,
                [ref]$seconds)) {
        # Parsed from an invariant-formatted string.
    }
    else {
        throw "Leg manifest entry $Index has a missing or unparsable '$Name'."
    }

    if ([double]::IsNaN($seconds) -or [double]::IsInfinity($seconds) -or $seconds -le 0) {
        throw "Leg manifest entry $Index has a non-positive '$Name'."
    }

    return $seconds
}

function ConvertTo-ReportCell {
    <#
    .SYNOPSIS
    Escape a manifest-supplied value for safe placement in Markdown.

    .DESCRIPTION
    Leg and image names come from the caller's manifest, so an unescaped pipe would silently add a
    column and corrupt the report table. Mirrors ConvertTo-MarkdownCell in
    eng/summarize-test-results.ps1 so both reports sanitize identically.
    #>
    param([AllowEmptyString()][string]$Value)

    $sanitized = $Value.Replace('\', '\\').Replace('|', '\|')
    $sanitized = [System.Text.RegularExpressions.Regex]::Replace($sanitized, '[\x00-\x1F\x7F]+', ' ')
    $sanitized = [System.Text.RegularExpressions.Regex]::Replace($sanitized, '\s{2,}', ' ')
    return $sanitized.Trim()
}

function Read-TrxCaseDuration {
    param([Parameter(Mandatory)][string]$Path)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.IgnoreComments = $true

    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($Path, $settings)
        $document.Load($reader)
    }
    catch {
        # Name the file and the underlying cause: one invocation reads every leg of every sampled
        # run, so a bare "malformed" verdict cannot be acted on, and a locked or permission-denied
        # file would otherwise be reported as malformed content.
        throw "Malformed MSTest TRX input '$Path': $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }

    if ($null -eq $document.DocumentElement -or
        $document.DocumentElement.LocalName -ne 'TestRun') {
        throw 'Input XML is not an MSTest TRX document.'
    }

    $definitions = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($unitTest in $document.SelectNodes(
        "/*[local-name()='TestRun']/*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
        $testId = $unitTest.GetAttribute('id')
        if ([string]::IsNullOrWhiteSpace($testId)) {
            throw 'Malformed MSTest TRX test definition.'
        }
        if (-not $definitions.Add($testId)) {
            throw 'Malformed MSTest TRX contains duplicate test definitions.'
        }
    }

    [long]$sumTicks = 0
    [int]$caseCount = 0
    foreach ($result in $document.SelectNodes(
        "/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']")) {
        $testId = $result.GetAttribute('testId')
        if ([string]::IsNullOrWhiteSpace($testId) -or -not $definitions.Contains($testId)) {
            throw 'Malformed MSTest TRX result cannot be joined to a test definition.'
        }

        [TimeSpan]$duration = [TimeSpan]::Zero
        $durationText = $result.GetAttribute('duration')
        if (-not [string]::IsNullOrWhiteSpace($durationText) -and
            -not [TimeSpan]::TryParse($durationText, $invariantCulture, [ref]$duration)) {
            throw 'Malformed MSTest TRX result contains an invalid duration.'
        }
        if ($duration.Ticks -lt 0) {
            throw 'Malformed MSTest TRX result contains a negative duration.'
        }

        $sumTicks += $duration.Ticks
        $caseCount++
    }

    if ($caseCount -eq 0) {
        throw "TRX file reports zero cases: '$Path'."
    }

    return [pscustomobject]@{
        SumSeconds = $sumTicks / [double][TimeSpan]::TicksPerSecond
        CaseCount = $caseCount
    }
}

if (-not (Test-Path -LiteralPath $ResultsRoot -PathType Container)) {
    throw "ResultsRoot is not an existing directory: '$ResultsRoot'."
}
$canonicalResultsRoot = [System.IO.Path]::GetFullPath($ResultsRoot)
$resultsRootPrefix = $canonicalResultsRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (-not (Test-Path -LiteralPath $LegManifest -PathType Leaf)) {
    throw "LegManifest is not an existing file: '$LegManifest'."
}

try {
    $manifestText = Get-Content -LiteralPath $LegManifest -Raw -Encoding UTF8
    $manifest = @($manifestText | ConvertFrom-Json -ErrorAction Stop)
}
catch {
    throw 'Leg manifest is not valid JSON.'
}

if ($manifest.Count -eq 0) {
    throw 'Leg manifest contains no leg observations.'
}

$observations = [System.Collections.Generic.List[object]]::new()
$seenPairs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$legImages = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::Ordinal)

for ($index = 0; $index -lt $manifest.Count; $index++) {
    $entry = $manifest[$index]
    if ($null -eq $entry -or $entry -isnot [System.Management.Automation.PSCustomObject]) {
        throw "Leg manifest entry $index is not an object."
    }

    $runId = Get-RequiredText -Entry $entry -Name 'runId' -Index $index
    $leg = Get-RequiredText -Entry $entry -Name 'leg' -Index $index
    $image = Get-RequiredText -Entry $entry -Name 'image' -Index $index
    $relativePath = Get-RequiredText -Entry $entry -Name 'path' -Index $index
    $wallTimeSeconds = Get-RequiredSeconds -Entry $entry -Name 'wallTimeSeconds' -Index $index

    if ($legImages.ContainsKey($leg)) {
        if ($legImages[$leg] -cne $image) {
            throw ("Leg '$leg' is claimed by two hosted images " +
                   "('$($legImages[$leg])' and '$image'). Hosted images are never merged.")
        }
    }
    else {
        $legImages.Add($leg, $image)
    }

    $pairKey = $runId + [char]0 + $leg
    if (-not $seenPairs.Add($pairKey)) {
        throw "Duplicate leg observation for run '$runId' leg '$leg'."
    }

    if ([System.IO.Path]::IsPathRooted($relativePath)) {
        throw "Leg manifest entry $index 'path' must be relative to ResultsRoot."
    }

    $resolvedPath = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($canonicalResultsRoot, $relativePath))
    if (-not $resolvedPath.StartsWith($resultsRootPrefix, [System.StringComparison]::Ordinal)) {
        throw "Leg manifest entry $index 'path' escapes ResultsRoot."
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        throw "Leg manifest entry $index 'path' is not an existing directory: '$relativePath'."
    }

    $trxFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $resolvedPath -File -Recurse) {
        if ([System.IO.Path]::GetExtension($file.Name).Equals(
                '.trx',
                [System.StringComparison]::OrdinalIgnoreCase)) {
            $trxFiles.Add([System.IO.Path]::GetFullPath($file.FullName))
        }
    }
    if ($trxFiles.Count -eq 0) {
        throw "Leg manifest entry $index 'path' holds no TRX files: '$relativePath'."
    }
    $trxFiles.Sort([System.StringComparer]::Ordinal)

    [double]$sumSeconds = 0
    [int]$caseCount = 0
    foreach ($trxFile in $trxFiles) {
        $parsed = Read-TrxCaseDuration -Path $trxFile
        $sumSeconds += $parsed.SumSeconds
        $caseCount += $parsed.CaseCount
    }

    $observations.Add([pscustomobject]@{
        RunId = $runId
        Leg = $leg
        Image = $image
        WallTimeSeconds = $wallTimeSeconds
        CaseDurationSeconds = $sumSeconds
        CaseCount = $caseCount
    })
}

$imageNames = [System.Collections.Generic.List[string]]::new()
foreach ($observation in $observations) {
    if (-not $imageNames.Contains($observation.Image)) {
        $imageNames.Add($observation.Image)
    }
}
$imageNames.Sort([System.StringComparer]::Ordinal)

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('## Hosted shard timing evidence')
$lines.Add('')
$lines.Add("Minimum samples per hosted image: $MinimumSamples. Wall time and summed TRX case " +
           'duration are reported separately and are not interchangeable.')

foreach ($imageName in $imageNames) {
    $imageObservations = @($observations | Where-Object { $_.Image -ceq $imageName })

    $runIds = [System.Collections.Generic.List[string]]::new()
    foreach ($observation in $imageObservations) {
        if (-not $runIds.Contains($observation.RunId)) {
            $runIds.Add($observation.RunId)
        }
    }
    $runIds.Sort([System.StringComparer]::Ordinal)

    if ($runIds.Count -lt $MinimumSamples) {
        throw ("Hosted image '$imageName' has $($runIds.Count) sampled runs, below the " +
               "required minimum of $MinimumSamples.")
    }

    $legNames = [System.Collections.Generic.List[string]]::new()
    foreach ($observation in $imageObservations) {
        if (-not $legNames.Contains($observation.Leg)) {
            $legNames.Add($observation.Leg)
        }
    }
    $legNames.Sort([System.StringComparer]::Ordinal)

    foreach ($runId in $runIds) {
        foreach ($legName in $legNames) {
            $present = @($imageObservations | Where-Object {
                $_.RunId -ceq $runId -and $_.Leg -ceq $legName
            })
            if ($present.Count -ne 1) {
                throw ("Run '$runId' is missing leg '$legName' of hosted image '$imageName'. " +
                       'Every sampled run must carry the complete leg set for its image.')
            }
        }
    }

    $legStatistics = [System.Collections.Generic.List[object]]::new()
    foreach ($legName in $legNames) {
        $legObservations = @($imageObservations | Where-Object { $_.Leg -ceq $legName })
        $wallValues = @($legObservations | ForEach-Object { $_.WallTimeSeconds })
        $caseDurationValues = @($legObservations | ForEach-Object { $_.CaseDurationSeconds })
        $caseCountValues = @($legObservations | ForEach-Object { $_.CaseCount })

        $wallMinimum = ($wallValues | Measure-Object -Minimum).Minimum
        $wallMaximum = ($wallValues | Measure-Object -Maximum).Maximum
        $caseDurationMinimum = ($caseDurationValues | Measure-Object -Minimum).Minimum
        $caseDurationMaximum = ($caseDurationValues | Measure-Object -Maximum).Maximum

        $legStatistics.Add([pscustomobject]@{
            Leg = $legName
            Runs = $legObservations.Count
            WallMean = ($wallValues | Measure-Object -Average).Average
            WallMinimum = $wallMinimum
            WallMaximum = $wallMaximum
            # Get-RequiredSeconds rejects a non-positive wall time, so the minimum is always
            # positive and this division needs no zero guard.
            WallSpread = $wallMaximum / $wallMinimum
            CaseDurationMean = ($caseDurationValues | Measure-Object -Average).Average
            CaseDurationMinimum = $caseDurationMinimum
            CaseDurationMaximum = $caseDurationMaximum
            CaseDurationSpread = if ($caseDurationMinimum -gt 0) {
                $caseDurationMaximum / $caseDurationMinimum
            }
            else {
                [double]::PositiveInfinity
            }
            CaseCountMinimum = ($caseCountValues | Measure-Object -Minimum).Minimum
            CaseCountMaximum = ($caseCountValues | Measure-Object -Maximum).Maximum
        })
    }

    $perRunCriticalPath = [System.Collections.Generic.List[double]]::new()
    $perRunBalancedFloor = [System.Collections.Generic.List[double]]::new()
    foreach ($runId in $runIds) {
        $runWallValues = @($imageObservations |
            Where-Object { $_.RunId -ceq $runId } |
            ForEach-Object { $_.WallTimeSeconds })
        $perRunCriticalPath.Add(($runWallValues | Measure-Object -Maximum).Maximum)
        $perRunBalancedFloor.Add(($runWallValues | Measure-Object -Average).Average)
    }

    $criticalPathMean = ($perRunCriticalPath | Measure-Object -Average).Average
    $balancedFloorMean = ($perRunBalancedFloor | Measure-Object -Average).Average
    $achievableGain = $criticalPathMean - $balancedFloorMean
    # Every wall time is positive (Get-RequiredSeconds), so the mean slowest leg is too.
    $achievableGainShare = 100.0 * $achievableGain / $criticalPathMean

    $noiseBand = (@($legStatistics | ForEach-Object {
        ($_.WallMaximum - $_.WallMinimum) / 2.0
    }) | Measure-Object -Average).Average

    $caseDurationMeans = @($legStatistics | ForEach-Object { $_.CaseDurationMean })
    $caseDurationMeanMinimum = ($caseDurationMeans | Measure-Object -Minimum).Minimum
    $betweenLegCaseSpread = if ($caseDurationMeanMinimum -gt 0) {
        ($caseDurationMeans | Measure-Object -Maximum).Maximum / $caseDurationMeanMinimum
    }
    else {
        [double]::PositiveInfinity
    }
    $worstSameLegCaseSpread = (@($legStatistics |
        ForEach-Object { $_.CaseDurationSpread }) | Measure-Object -Maximum).Maximum

    $caseDurationIsUsable = $worstSameLegCaseSpread -lt $betweenLegCaseSpread
    $gainExceedsNoise = $achievableGain -gt $noiseBand
    $adoptDurationWeights = $caseDurationIsUsable -and $gainExceedsNoise

    $lines.Add('')
    $lines.Add("### $(ConvertTo-ReportCell $imageName)")
    $lines.Add('')
    $lines.Add("Sampled runs: $($runIds.Count). Legs: $($legNames.Count).")
    $lines.Add('')
    $lines.Add('| Leg | Runs | Wall mean (s) | Wall min (s) | Wall max (s) | Wall spread | ' +
               'Case duration mean (s) | Case duration min (s) | Case duration max (s) | ' +
               'Case duration spread | Cases |')
    $lines.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($statistic in $legStatistics) {
        $caseCountText = if ($statistic.CaseCountMinimum -eq $statistic.CaseCountMaximum) {
            [string]$statistic.CaseCountMinimum
        }
        else {
            "$($statistic.CaseCountMinimum)-$($statistic.CaseCountMaximum)"
        }
        $lines.Add(
            "| $(ConvertTo-ReportCell $statistic.Leg) | $($statistic.Runs) | " +
            "$(Format-Seconds $statistic.WallMean) | " +
            "$(Format-Seconds $statistic.WallMinimum) | " +
            "$(Format-Seconds $statistic.WallMaximum) | " +
            "$(Format-Ratio $statistic.WallSpread) | " +
            "$(Format-Seconds $statistic.CaseDurationMean) | " +
            "$(Format-Seconds $statistic.CaseDurationMinimum) | " +
            "$(Format-Seconds $statistic.CaseDurationMaximum) | " +
            "$(Format-Ratio $statistic.CaseDurationSpread) | $caseCountText |")
    }
    $lines.Add('')
    $lines.Add('| Image metric | Value |')
    $lines.Add('| --- | ---: |')
    $lines.Add("| Critical path, mean slowest leg | $(Format-Seconds $criticalPathMean) s |")
    $lines.Add("| Balanced floor, mean leg average | $(Format-Seconds $balancedFloorMean) s |")
    $lines.Add("| Achievable gain from a perfect partition | " +
               "$(Format-Seconds $achievableGain) s " +
               "($($achievableGainShare.ToString('0.0', $invariantCulture))%) |")
    $lines.Add("| Single-leg wall-time noise band | $(Format-Seconds $noiseBand) s |")
    $lines.Add("| Worst same-leg case-duration spread | $(Format-Ratio $worstSameLegCaseSpread) |")
    $lines.Add("| Between-leg case-duration spread | $(Format-Ratio $betweenLegCaseSpread) |")
    $lines.Add('')

    if ($caseDurationIsUsable) {
        $lines.Add('- Summed TRX case duration is reproducible enough on this image to rank legs.')
    }
    else {
        $lines.Add('- Summed TRX case duration is NOT a partition signal for this image: the ' +
                   'same-leg run-to-run swing meets or exceeds the between-leg spread.')
    }

    if ($gainExceedsNoise) {
        $lines.Add('- The achievable gain from a perfect duration partition exceeds the ' +
                   'single-leg wall-time noise band.')
    }
    else {
        $lines.Add('- The achievable gain from a perfect duration partition does not exceed the ' +
                   'single-leg wall-time noise band.')
    }

    $lines.Add('')
    if ($adoptDurationWeights) {
        $lines.Add("**Verdict for $(ConvertTo-ReportCell $imageName): material skew. An OS-specific duration profile is " +
                   'warranted.**')
    }
    else {
        $lines.Add("**Verdict for $(ConvertTo-ReportCell $imageName): no material skew. Keep the deterministic " +
                   'discovered-case weights in `eng/get-test-shard-plan.ps1`.**')
    }
}

[Console]::Out.WriteLine($lines -join [Environment]::NewLine)
