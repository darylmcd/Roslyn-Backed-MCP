<#
.SYNOPSIS
Build a deterministic, fail-closed class-level MSTest shard plan.

.DESCRIPTION
Reads the compiled test assembly through MetadataLoadContext from the active
.NET SDK. Each concrete [TestClass] is weighted by its statically discoverable
[TestMethod]/[DataTestMethod] cases; [DataRow] methods contribute one case per
row and every other test method contributes one case. Classes are assigned with
deterministic longest-processing-time-first greedy balancing.

The JSON result contains every shard plus the selected shard's exact
ClassName filter. Discovery or integrity failures terminate with a nonzero exit.
When AdapterResultsPath names a structured TRX file from the installed adapter,
the metadata catalog must exactly match the adapter-discovered class catalog.

.PARAMETER ShardBuildSeconds
Optional Release build durations in shard-index order for an ubuntu-latest
hosted duration plan. Longer builds reduce the predicted test capacity.
#>
param(
    [Parameter(Mandatory)]
    [string]$TestAssemblyPath,

    [int]$TestShardCount = 1,

    [int]$TestShardIndex = 0,

    [string]$AdapterResultsPath,

    [string]$DurationProfilePath,

    [string]$HostedImage,

    [double[]]$ShardBuildSeconds
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotnetQuery {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $global:LASTEXITCODE = 0
    $output = @(& dotnet @Arguments)
    $exitCode = $global:LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }

    return $output
}

function Test-AttributeType {
    param(
        [Parameter(Mandatory)]
        [System.Type]$AttributeType,

        [Parameter(Mandatory)]
        [System.Collections.Generic.HashSet[string]]$ExpectedTypeNames
    )

    $candidateType = $AttributeType
    while ($null -ne $candidateType) {
        if ($null -ne $candidateType.FullName -and $ExpectedTypeNames.Contains($candidateType.FullName)) {
            return $true
        }
        $candidateType = $candidateType.BaseType
    }

    return $false
}

function Format-ClassNameSample {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ClassNames
    )

    $sample = @($ClassNames | Sort-Object | Select-Object -First 10)
    $suffix = if ($ClassNames.Count -gt $sample.Count) { ', ...' } else { '' }
    return ($sample -join ', ') + $suffix
}

if ($TestShardCount -lt 1 -or $TestShardCount -gt 16) {
    throw "TestShardCount must be between 1 and 16; received $TestShardCount."
}

if ($TestShardIndex -lt 0 -or $TestShardIndex -ge $TestShardCount) {
    throw "TestShardIndex must be between 0 and $($TestShardCount - 1); received $TestShardIndex."
}
if ([string]::IsNullOrWhiteSpace($DurationProfilePath) -ne
    [string]::IsNullOrWhiteSpace($HostedImage)) {
    throw 'DurationProfilePath and HostedImage must be supplied together.'
}

$buildTimesSupplied = $PSBoundParameters.ContainsKey('ShardBuildSeconds')
$useBuildTimes = $false
$buildTimeExponent = 0.75
$buildTimeOffsets = @()
if ($buildTimesSupplied) {
    if ($HostedImage -cne 'ubuntu-latest' -or [string]::IsNullOrWhiteSpace($DurationProfilePath)) {
        throw 'ShardBuildSeconds requires an Ubuntu hosted duration profile.'
    }
    if ($TestShardCount -lt 2) {
        throw 'ShardBuildSeconds requires at least two shards.'
    }
    if ($null -eq $ShardBuildSeconds -or $ShardBuildSeconds.Count -ne $TestShardCount) {
        throw "ShardBuildSeconds must contain exactly $TestShardCount entries in shard-index order."
    }
    foreach ($seconds in $ShardBuildSeconds) {
        if (-not [double]::IsFinite($seconds) -or $seconds -le 0) {
            throw 'Every ShardBuildSeconds entry must be finite and strictly positive.'
        }
    }

    $useBuildTimes = @($ShardBuildSeconds -ne $ShardBuildSeconds[0]).Count -gt 0
    # Compare log((assignedWeight + classWeight) * (buildSeconds / median)^exponent).
    # The median contributes the same term to every score, so drop it. Comparing
    # in log space also avoids overflow and underflow for extreme build durations.
    $buildTimeOffsets = @(foreach ($seconds in $ShardBuildSeconds) {
        $buildTimeExponent * [Math]::Log($seconds)
    })
}

$canonicalAssemblyPath = [System.IO.Path]::GetFullPath($TestAssemblyPath)
if (-not (Test-Path -LiteralPath $canonicalAssemblyPath -PathType Leaf)) {
    throw "Test assembly not found: $canonicalAssemblyPath"
}

$sdkVersionLines = Invoke-DotnetQuery -Description 'dotnet SDK version query' -Arguments @('--version')
$sdkVersion = ($sdkVersionLines | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw 'The active dotnet SDK version query returned no version.'
}

$sdkEntries = Invoke-DotnetQuery -Description 'dotnet SDK inventory query' -Arguments @('--list-sdks')
# System.Reflection.MetadataLoadContext is loaded into this PowerShell process, so the copy must
# target a runtime no newer than the one PowerShell runs on. The SDK that global.json selects can
# be a major version ahead of it (SDK 11 building net10.0 projects under PowerShell on .NET 10).
# Prefer the active SDK; otherwise take the newest installed SDK this runtime can load.
$hostRuntimeMajor = [System.Environment]::Version.Major
$loadableSdks = @(
    foreach ($entry in $sdkEntries) {
        if ($entry -notmatch '^([^\s]+)\s+\[(.+)\]\s*$') { continue }
        $entryVersion = $Matches[1]
        $entryRoot = $Matches[2]
        $parsedVersion = $null
        if (-not [System.Version]::TryParse(($entryVersion -split '-', 2)[0], [ref]$parsedVersion)) { continue }
        if ($parsedVersion.Major -gt $hostRuntimeMajor) { continue }
        $candidatePath = Join-Path (Join-Path $entryRoot $entryVersion) 'System.Reflection.MetadataLoadContext.dll'
        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { continue }
        [pscustomobject]@{
            Version = $entryVersion
            Parsed = $parsedVersion
            IsPrerelease = $entryVersion.Contains('-')
            Path = $candidatePath
        }
    }
)
$selectedSdk = @($loadableSdks | Where-Object Version -eq $sdkVersion) | Select-Object -First 1
if ($null -eq $selectedSdk) {
    $selectedSdk = $loadableSdks |
        Sort-Object -Property @{ Expression = 'Parsed'; Descending = $true }, @{ Expression = 'IsPrerelease' } |
        Select-Object -First 1
}
if ($null -eq $selectedSdk) {
    throw "No installed dotnet SDK provides a System.Reflection.MetadataLoadContext.dll that PowerShell on .NET $hostRuntimeMajor can load. Active SDK: '$sdkVersion'. Install an SDK with major version $hostRuntimeMajor or lower, or run a PowerShell built on a newer .NET."
}
$metadataLoadContextPath = $selectedSdk.Path

Add-Type -Path $metadataLoadContextPath

$pathComparer = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$resolverPaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$assemblyDirectory = Split-Path -Parent $canonicalAssemblyPath
$runtimeDirectory = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
foreach ($directory in @($assemblyDirectory, $runtimeDirectory)) {
    foreach ($assemblyFile in Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File) {
        [void]$resolverPaths.Add($assemblyFile.FullName)
    }
}
[void]$resolverPaths.Add($canonicalAssemblyPath)
[void]$resolverPaths.Add([System.IO.Path]::GetFullPath($metadataLoadContextPath))

$resolver = [System.Reflection.PathAssemblyResolver]::new([string[]]$resolverPaths)
$metadataContext = [System.Reflection.MetadataLoadContext]::new($resolver)

$testClassAttributes = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
[void]$testClassAttributes.Add('Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute')
$testMethodAttributes = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
[void]$testMethodAttributes.Add('Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute')
[void]$testMethodAttributes.Add('Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute')
$dataRowAttributes = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
[void]$dataRowAttributes.Add('Microsoft.VisualStudio.TestTools.UnitTesting.DataRowAttribute')
$bindingFlags = [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Static

$testClasses = [System.Collections.Generic.List[object]]::new()
try {
    $testAssembly = $metadataContext.LoadFromAssemblyPath($canonicalAssemblyPath)
    foreach ($type in $testAssembly.GetTypes()) {
        if (-not $type.IsClass -or $type.IsAbstract) {
            continue
        }

        $isTestClass = $false
        foreach ($attribute in $type.GetCustomAttributesData()) {
            if (Test-AttributeType -AttributeType $attribute.AttributeType -ExpectedTypeNames $testClassAttributes) {
                $isTestClass = $true
                break
            }
        }
        if (-not $isTestClass) {
            continue
        }

        $className = $type.FullName
        if ([string]::IsNullOrWhiteSpace($className)) {
            throw "A concrete test class in '$canonicalAssemblyPath' has no full name."
        }
        if ($className -notmatch '\A[A-Za-z_][A-Za-z0-9_.]*\z') {
            throw "Test class '$className' cannot be represented safely in an exact ClassName filter."
        }

        $staticCaseWeight = 0
        foreach ($method in $type.GetMethods($bindingFlags)) {
            $methodAttributes = @($method.GetCustomAttributesData())
            $isTestMethod = $false
            $dataRowCount = 0
            foreach ($attribute in $methodAttributes) {
                if (Test-AttributeType -AttributeType $attribute.AttributeType -ExpectedTypeNames $testMethodAttributes) {
                    $isTestMethod = $true
                }
                if (Test-AttributeType -AttributeType $attribute.AttributeType -ExpectedTypeNames $dataRowAttributes) {
                    $dataRowCount++
                }
            }

            if ($isTestMethod) {
                $staticCaseWeight += [Math]::Max(1, $dataRowCount)
            }
        }

        if ($staticCaseWeight -gt 0) {
            $testClasses.Add([pscustomobject]@{
                ClassName = $className
                StaticCaseWeight = $staticCaseWeight
            })
        }
    }
}
finally {
    $metadataContext.Dispose()
}

if ($testClasses.Count -eq 0) {
    throw "No runnable concrete MSTest classes were discovered in '$canonicalAssemblyPath'."
}
if ($TestShardCount -gt $testClasses.Count) {
    throw "TestShardCount ($TestShardCount) exceeds the discovered test-class count ($($testClasses.Count))."
}

$adapterClassCount = 0
$adapterParityVerified = $false
if (-not [string]::IsNullOrWhiteSpace($AdapterResultsPath)) {
    $canonicalAdapterResultsPath = [System.IO.Path]::GetFullPath($AdapterResultsPath)
    if (-not (Test-Path -LiteralPath $canonicalAdapterResultsPath -PathType Leaf)) {
        throw "Adapter TRX results not found: $canonicalAdapterResultsPath"
    }

    [xml]$adapterResults = Get-Content -Raw -LiteralPath $canonicalAdapterResultsPath
    $adapterClassNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $testMethodNodes = @($adapterResults.SelectNodes(
        "//*[local-name()='UnitTest']/*[local-name()='TestMethod']"))
    foreach ($testMethodNode in $testMethodNodes) {
        $className = $testMethodNode.GetAttribute('className')
        if ([string]::IsNullOrWhiteSpace($className)) {
            throw "Adapter TRX '$canonicalAdapterResultsPath' contains a test definition without a className."
        }
        if ($className -notmatch '\A[A-Za-z_][A-Za-z0-9_.]*\z') {
            throw "Adapter-discovered class '$className' cannot be represented safely in an exact ClassName filter."
        }
        [void]$adapterClassNames.Add($className)
    }
    if ($adapterClassNames.Count -eq 0) {
        throw "Adapter TRX '$canonicalAdapterResultsPath' contains no runnable test-class definitions."
    }

    $plannerClassNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($testClass in $testClasses) {
        [void]$plannerClassNames.Add($testClass.ClassName)
    }

    $omittedByPlanner = @(
        $adapterClassNames |
            Where-Object { -not $plannerClassNames.Contains($_) }
    )
    $unselectablePlannerClasses = @(
        $plannerClassNames |
            Where-Object { -not $adapterClassNames.Contains($_) }
    )
    if ($omittedByPlanner.Count -gt 0 -or $unselectablePlannerClasses.Count -gt 0) {
        $omittedSample = Format-ClassNameSample -ClassNames $omittedByPlanner
        $unselectableSample = Format-ClassNameSample -ClassNames $unselectablePlannerClasses
        throw "Adapter parity failed. Adapter classes omitted by planner: [$omittedSample]. Planner classes absent from adapter results: [$unselectableSample]."
    }

    $adapterClassCount = $adapterClassNames.Count
    $adapterParityVerified = $true
}

$weightingMode = 'static-cases'
$profiledClassCount = 0
$fallbackClassCount = 0
if (-not [string]::IsNullOrWhiteSpace($DurationProfilePath)) {
    if ($HostedImage -cnotin @('windows-latest', 'ubuntu-latest')) {
        throw "Unsupported hosted image '$HostedImage'."
    }
    if (-not (Test-Path -LiteralPath $DurationProfilePath -PathType Leaf)) {
        throw "Duration profile not found: '$DurationProfilePath'."
    }
    try {
        $profile = Get-Content -LiteralPath $DurationProfilePath -Raw -Encoding UTF8 |
            ConvertFrom-Json -ErrorAction Stop
    }
    catch { throw "Duration profile is invalid JSON: $($_.Exception.Message)" }
    if ($profile.SchemaVersion -ne 1 -or $profile.MinimumSamples -lt 5 -or
        $null -eq $profile.Images -or $profile.Images.Count -eq 0) {
        throw 'Duration profile has an invalid schema or insufficient sample requirement.'
    }
    $seenImages = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $selectedImage = $null
    foreach ($image in $profile.Images) {
        if ($image.Image -cnotin @('windows-latest', 'ubuntu-latest') -or
            -not $seenImages.Add($image.Image)) {
            throw 'Duration profile contains an unknown or duplicate hosted image.'
        }
        if ($image.Image -ceq $HostedImage) { $selectedImage = $image }
    }
    if ($null -eq $selectedImage) {
        throw "Duration profile has no '$HostedImage' image."
    }
    $runIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($runId in $selectedImage.RunIds) {
        if ([string]::IsNullOrWhiteSpace($runId) -or -not $runIds.Add($runId)) {
            throw "Duration profile '$HostedImage' has an empty or duplicate run ID."
        }
    }
    if ($runIds.Count -lt $profile.MinimumSamples) {
        throw "Duration profile '$HostedImage' has fewer than $($profile.MinimumSamples) source runs."
    }
    $profileClasses = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($entry in $selectedImage.Classes) {
        if ($entry.ClassName -notmatch '\A[A-Za-z_][A-Za-z0-9_.]*\z' -or
            $entry.StaticCaseWeight -le 0 -or
            $entry.MeanCaseSeconds -le 0 -or
            [double]::IsNaN([double]$entry.MeanCaseSeconds) -or
            [double]::IsInfinity([double]$entry.MeanCaseSeconds) -or
            $profileClasses.ContainsKey($entry.ClassName)) {
            throw "Duration profile '$HostedImage' contains an invalid or duplicate class."
        }
        $profileClasses.Add($entry.ClassName, $entry)
    }
    [double]$profileSeconds = 0
    [long]$profileStaticCases = 0
    $entry = $null
    foreach ($testClass in $testClasses) {
        if ($profileClasses.TryGetValue($testClass.ClassName, [ref]$entry) -and
            $entry.StaticCaseWeight -eq $testClass.StaticCaseWeight) {
            $profileSeconds += [double]$entry.MeanCaseSeconds
            $profileStaticCases += $testClass.StaticCaseWeight
        }
    }
    if ($profileStaticCases -eq 0 -or $profileSeconds -le 0) {
        throw "Duration profile '$HostedImage' has no matching current classes."
    }
    $fallbackSecondsPerCase = $profileSeconds / $profileStaticCases
    foreach ($testClass in $testClasses) {
        $entry = $null
        if ($profileClasses.TryGetValue($testClass.ClassName, [ref]$entry) -and
            $entry.StaticCaseWeight -eq $testClass.StaticCaseWeight) {
            $balanceWeight = [double]$entry.MeanCaseSeconds
            $profiledClassCount++
        }
        else {
            $balanceWeight = $testClass.StaticCaseWeight * $fallbackSecondsPerCase
            $fallbackClassCount++
        }
        $testClass | Add-Member -NotePropertyName BalanceWeight -NotePropertyValue $balanceWeight
    }
    $weightingMode = 'hosted-duration'
}
else {
    foreach ($testClass in $testClasses) {
        $testClass | Add-Member -NotePropertyName BalanceWeight -NotePropertyValue ([double]$testClass.StaticCaseWeight)
    }
}

$assignmentOrder = [System.Collections.Generic.List[object]]::new()
$assignmentOrder.AddRange($testClasses)
$assignmentOrder.Sort([System.Comparison[object]]{
    param($left, $right)

    $weightComparison = $right.BalanceWeight.CompareTo($left.BalanceWeight)
    if ($weightComparison -ne 0) {
        return $weightComparison
    }

    return [System.StringComparer]::Ordinal.Compare($left.ClassName, $right.ClassName)
})

$mutableShards = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $TestShardCount; $index++) {
    $mutableShards.Add([pscustomobject]@{
        Index = $index
        StaticCaseWeight = 0
        BalanceWeight = [double]0
        Classes = [System.Collections.Generic.List[object]]::new()
    })
}

$emptyShardCount = $TestShardCount
for ($assignmentIndex = 0; $assignmentIndex -lt $assignmentOrder.Count; $assignmentIndex++) {
    $testClass = $assignmentOrder[$assignmentIndex]
    $mustFillEmptyShard = $assignmentOrder.Count - $assignmentIndex -eq $emptyShardCount
    $selectedShard = $null
    $selectedScore = [double]::PositiveInfinity
    foreach ($candidate in $mutableShards) {
        if ($mustFillEmptyShard -and $candidate.Classes.Count -ne 0) {
            continue
        }
        $score = if ($useBuildTimes) {
            [Math]::Log($candidate.BalanceWeight + $testClass.BalanceWeight) + $buildTimeOffsets[$candidate.Index]
        }
        else {
            $candidate.BalanceWeight
        }
        # Candidates are visited in shard-index order. Keep the first in a tie.
        if ($null -eq $selectedShard -or $score -lt $selectedScore) {
            $selectedShard = $candidate
            $selectedScore = $score
        }
    }

    if ($selectedShard.Classes.Count -eq 0) {
        $emptyShardCount--
    }
    $selectedShard.Classes.Add($testClass)
    $selectedShard.StaticCaseWeight += $testClass.StaticCaseWeight
    $selectedShard.BalanceWeight += $testClass.BalanceWeight
}

$assignedClassNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$assignedWeight = 0
$shards = [System.Collections.Generic.List[object]]::new()
foreach ($mutableShard in $mutableShards) {
    if ($mutableShard.Classes.Count -eq 0) {
        throw "Shard $($mutableShard.Index) is empty. Reduce TestShardCount."
    }

    $classNames = [System.Collections.Generic.List[string]]::new()
    foreach ($testClass in $mutableShard.Classes) {
        if (-not $assignedClassNames.Add($testClass.ClassName)) {
            throw "Test class '$($testClass.ClassName)' was assigned to more than one shard."
        }
        $assignedWeight += $testClass.StaticCaseWeight
        $classNames.Add($testClass.ClassName)
    }
    $classNames.Sort([System.StringComparer]::Ordinal)

    $exactFilter = (@($classNames | ForEach-Object { "ClassName=$_" })) -join '|'
    if ([string]::IsNullOrWhiteSpace($exactFilter)) {
        throw "Shard $($mutableShard.Index) produced an empty test filter."
    }

    $shards.Add([pscustomobject]@{
        Index = $mutableShard.Index
        ClassCount = $classNames.Count
        StaticCaseWeight = $mutableShard.StaticCaseWeight
        BalanceWeight = $mutableShard.BalanceWeight
        Classes = @($classNames)
        Filter = $exactFilter
    })
}

$expectedWeight = [int](($testClasses | Measure-Object -Property StaticCaseWeight -Sum).Sum)
if ($assignedClassNames.Count -ne $testClasses.Count -or $assignedWeight -ne $expectedWeight) {
    throw 'The shard plan is incomplete: assigned class count or static case weight does not match discovery.'
}

$classCatalog = [System.Collections.Generic.List[object]]::new()
$classCatalog.AddRange($testClasses)
$classCatalog.Sort([System.Comparison[object]]{
    param($left, $right)
    return [System.StringComparer]::Ordinal.Compare($left.ClassName, $right.ClassName)
})

$plan = [pscustomobject]@{
    SchemaVersion = 1
    TestAssemblyPath = $canonicalAssemblyPath
    TestShardCount = $TestShardCount
    SelectedShardIndex = $TestShardIndex
    TotalClassCount = $testClasses.Count
    TotalStaticCaseWeight = $expectedWeight
    AdapterParityVerified = $adapterParityVerified
    AdapterClassCount = $adapterClassCount
    WeightingMode = $weightingMode
    ProfiledClassCount = $profiledClassCount
    FallbackClassCount = $fallbackClassCount
    TestClasses = @($classCatalog)
    Shards = @($shards)
    SelectedFilter = $shards[$TestShardIndex].Filter
}

if ($buildTimesSupplied) {
    $plan | Add-Member -NotePropertyName ShardBuildSeconds -NotePropertyValue $ShardBuildSeconds
    $plan | Add-Member -NotePropertyName ShardBuildTimeExponent -NotePropertyValue $buildTimeExponent
}

$plan | ConvertTo-Json -Depth 8 -Compress
