<#
.SYNOPSIS
Render deterministic timing and outcome summaries from MSTest TRX files.

.DESCRIPTION
ResultsPath accepts one or more TRX files or directories. Directories are
searched recursively. The summary joins UnitTestResult entries to UnitTest
definitions by testId, aggregates parameterized cases by method, and emits
Markdown without source paths or TRX codeBase metadata.

When OutputPath is omitted, Markdown is written to stdout. When OutputPath is
provided, one complete summary is appended in UTF-8 so an existing GitHub step
summary is preserved. Missing paths or paths containing no TRX files produce a
clear no-results summary and exit successfully. Malformed TRX input fails.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$ResultsPath,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-MarkdownCell {
    param([AllowEmptyString()][string]$Value)

    $sanitized = $Value.Replace('\', '\\').Replace('|', '\|')
    $sanitized = [System.Text.RegularExpressions.Regex]::Replace(
        $sanitized,
        '[\x00-\x1F\x7F]+',
        ' ')
    $sanitized = [System.Text.RegularExpressions.Regex]::Replace(
        $sanitized,
        '\s{2,}',
        ' ')
    $sanitized = $sanitized.Trim()
    if ([string]::IsNullOrEmpty($sanitized)) {
        return '(unnamed)'
    }

    $maximumCellLength = 240
    if ($sanitized.Length -gt $maximumCellLength) {
        $sanitized = $sanitized.Substring(0, $maximumCellLength - 3) + '...'
    }

    $sanitized = [System.Net.WebUtility]::HtmlEncode($sanitized)
    return $sanitized
}

function Format-Duration {
    param([long]$Ticks)

    $roundedMilliseconds = [Math]::Round(
        $Ticks / [double][TimeSpan]::TicksPerMillisecond,
        [MidpointRounding]::AwayFromZero)
    $text = [TimeSpan]::FromMilliseconds($roundedMilliseconds).ToString(
        'c',
        [System.Globalization.CultureInfo]::InvariantCulture)
    if ($text.Contains('.')) {
        $text = $text.TrimEnd('0').TrimEnd('.')
    }

    return $text
}

function Publish-Summary {
    param([Parameter(Mandatory)][string]$Markdown)

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        [Console]::Out.WriteLine($Markdown)
        return
    }

    $canonicalOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($canonicalOutputPath)
    if (-not [string]::IsNullOrEmpty($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    $prefix = if ([System.IO.File]::Exists($canonicalOutputPath) -and
                  [System.IO.FileInfo]::new($canonicalOutputPath).Length -gt 0) {
        [Environment]::NewLine
    }
    else {
        ''
    }
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::AppendAllText(
        $canonicalOutputPath,
        $prefix + $Markdown.TrimEnd() + [Environment]::NewLine,
        $utf8WithoutBom)
}

function Read-TrxCases {
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
        throw 'Malformed MSTest TRX input.'
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

    $definitions = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($unitTest in $document.SelectNodes(
        "/*[local-name()='TestRun']/*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
        $testId = $unitTest.GetAttribute('id')
        $testMethodNodes = @($unitTest.SelectNodes("./*[local-name()='TestMethod']"))
        if ([string]::IsNullOrWhiteSpace($testId) -or $testMethodNodes.Count -ne 1) {
            throw 'Malformed MSTest TRX test definition.'
        }

        $className = $testMethodNodes[0].GetAttribute('className')
        $methodName = $testMethodNodes[0].GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($className) -or
            [string]::IsNullOrWhiteSpace($methodName)) {
            throw 'Malformed MSTest TRX test method definition.'
        }
        if ($definitions.ContainsKey($testId)) {
            throw 'Malformed MSTest TRX contains duplicate test definitions.'
        }

        $definitions.Add($testId, [pscustomobject]@{
            ClassName = $className
            MethodName = $methodName
        })
    }

    foreach ($result in $document.SelectNodes(
        "/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']")) {
        $testId = $result.GetAttribute('testId')
        $outcome = $result.GetAttribute('outcome')
        if ([string]::IsNullOrWhiteSpace($testId) -or
            [string]::IsNullOrWhiteSpace($outcome) -or
            -not $definitions.ContainsKey($testId)) {
            throw 'Malformed MSTest TRX result cannot be joined to a test definition.'
        }

        [TimeSpan]$duration = [TimeSpan]::Zero
        $durationText = $result.GetAttribute('duration')
        if (-not [string]::IsNullOrWhiteSpace($durationText) -and
            -not [TimeSpan]::TryParse(
                $durationText,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$duration)) {
            throw 'Malformed MSTest TRX result contains an invalid duration.'
        }
        if ($duration.Ticks -lt 0) {
            throw 'Malformed MSTest TRX result contains a negative duration.'
        }

        $definition = $definitions[$testId]
        [pscustomobject]@{
            ClassName = $definition.ClassName
            MethodName = $definition.MethodName
            DurationTicks = $duration.Ticks
            Outcome = $outcome
        }
    }
}

function Get-TimingAggregates {
    param(
        [Parameter(Mandatory)][object[]]$Cases,
        [Parameter(Mandatory)][ValidateSet('Method', 'Class')][string]$GroupBy
    )

    $aggregates = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($testCase in $Cases) {
        $key = if ($GroupBy -eq 'Method') {
            $testCase.ClassName + [char]0 + $testCase.MethodName
        }
        else {
            $testCase.ClassName
        }
        $displayName = if ($GroupBy -eq 'Method') {
            $testCase.ClassName + '.' + $testCase.MethodName
        }
        else {
            $testCase.ClassName
        }

        if (-not $aggregates.ContainsKey($key)) {
            $aggregates.Add($key, [pscustomobject]@{
                DisplayName = $displayName
                SumTicks = [long]0
                CaseCount = 0
                MaxTicks = [long]0
            })
        }

        $aggregate = $aggregates[$key]
        $aggregate.SumTicks += $testCase.DurationTicks
        $aggregate.CaseCount++
        if ($testCase.DurationTicks -gt $aggregate.MaxTicks) {
            $aggregate.MaxTicks = $testCase.DurationTicks
        }
    }

    $ordered = [System.Collections.Generic.List[object]]::new()
    foreach ($aggregate in $aggregates.Values) {
        $ordered.Add($aggregate)
    }
    $ordered.Sort([System.Comparison[object]]{
        param($left, $right)

        $durationComparison = $right.SumTicks.CompareTo($left.SumTicks)
        if ($durationComparison -ne 0) {
            return $durationComparison
        }

        return [System.StringComparer]::Ordinal.Compare(
            $left.DisplayName,
            $right.DisplayName)
    })

    return $ordered
}

$noResultsMarkdown = @'
## Test timing summary

No MSTest TRX results were found. The test command may have exited before result generation.
'@

$pathComparer = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$discoveredPaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($candidatePath in $ResultsPath) {
    if ([string]::IsNullOrWhiteSpace($candidatePath) -or
        -not (Test-Path -LiteralPath $candidatePath)) {
        continue
    }

    $item = Get-Item -LiteralPath $candidatePath
    if ($item.PSIsContainer) {
        foreach ($file in Get-ChildItem -LiteralPath $item.FullName -File -Recurse) {
            if ([System.IO.Path]::GetExtension($file.Name).Equals(
                    '.trx',
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$discoveredPaths.Add([System.IO.Path]::GetFullPath($file.FullName))
            }
        }
    }
    elseif ([System.IO.Path]::GetExtension($item.Name).Equals(
            '.trx',
            [System.StringComparison]::OrdinalIgnoreCase)) {
        [void]$discoveredPaths.Add([System.IO.Path]::GetFullPath($item.FullName))
    }
}

$trxFiles = [System.Collections.Generic.List[string]]::new()
foreach ($path in $discoveredPaths) {
    $trxFiles.Add($path)
}
$trxFiles.Sort($pathComparer)

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $canonicalOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    foreach ($trxPath in $trxFiles) {
        if ($pathComparer.Equals($canonicalOutputPath, $trxPath)) {
            throw 'OutputPath must not overwrite or append to an input TRX file.'
        }
    }
}

if ($trxFiles.Count -eq 0) {
    Publish-Summary -Markdown $noResultsMarkdown
    exit 0
}

$testCases = [System.Collections.Generic.List[object]]::new()
foreach ($trxFile in $trxFiles) {
    foreach ($testCase in @(Read-TrxCases -Path $trxFile)) {
        $testCases.Add($testCase)
    }
}

if ($testCases.Count -eq 0) {
    Publish-Summary -Markdown $noResultsMarkdown
    exit 0
}

$passedCount = 0
$failedCount = 0
$skippedCount = 0
$otherCount = 0
$totalTicks = [long]0
foreach ($testCase in $testCases) {
    $totalTicks += $testCase.DurationTicks
    switch ($testCase.Outcome.ToUpperInvariant()) {
        'PASSED' {
            $passedCount++
        }
        { $_ -in @('FAILED', 'ERROR', 'TIMEOUT', 'ABORTED', 'NOTRUNNABLE', 'DISCONNECTED') } {
            $failedCount++
        }
        { $_ -in @('NOTEXECUTED', 'SKIPPED', 'INCONCLUSIVE', 'PENDING') } {
            $skippedCount++
        }
        default {
            $otherCount++
        }
    }
}

$methodAggregates = @(Get-TimingAggregates -Cases @($testCases) -GroupBy Method)
$classAggregates = @(Get-TimingAggregates -Cases @($testCases) -GroupBy Class)
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('## Test timing summary')
$lines.Add('')
$lines.Add('| Total cases | Passed | Failed | Skipped | Other | Summed duration |')
$lines.Add('| ---: | ---: | ---: | ---: | ---: | ---: |')
$lines.Add("| $($testCases.Count) | $passedCount | $failedCount | $skippedCount | $otherCount | $(Format-Duration $totalTicks) |")
$lines.Add('')
$lines.Add('### Top 15 slowest methods')
$lines.Add('')
$lines.Add('| Method | Sum duration | Cases | Max duration |')
$lines.Add('| --- | ---: | ---: | ---: |')
foreach ($aggregate in @($methodAggregates | Select-Object -First 15)) {
    $lines.Add(
        "| $(ConvertTo-MarkdownCell $aggregate.DisplayName) | " +
        "$(Format-Duration $aggregate.SumTicks) | $($aggregate.CaseCount) | " +
        "$(Format-Duration $aggregate.MaxTicks) |")
}
$lines.Add('')
$lines.Add('### Top 15 slowest classes')
$lines.Add('')
$lines.Add('| Class | Sum duration | Cases | Max duration |')
$lines.Add('| --- | ---: | ---: | ---: |')
foreach ($aggregate in @($classAggregates | Select-Object -First 15)) {
    $lines.Add(
        "| $(ConvertTo-MarkdownCell $aggregate.DisplayName) | " +
        "$(Format-Duration $aggregate.SumTicks) | $($aggregate.CaseCount) | " +
        "$(Format-Duration $aggregate.MaxTicks) |")
}

Publish-Summary -Markdown ($lines -join [Environment]::NewLine)
