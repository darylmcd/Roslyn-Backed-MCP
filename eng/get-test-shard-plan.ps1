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
#>
param(
    [Parameter(Mandatory)]
    [string]$TestAssemblyPath,

    [int]$TestShardCount = 1,

    [int]$TestShardIndex = 0,

    [string]$AdapterResultsPath
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
$matchingSdkDirectories = @(
    foreach ($entry in $sdkEntries) {
        if ($entry -match '^([^\s]+)\s+\[(.+)\]\s*$' -and $Matches[1] -eq $sdkVersion) {
            Join-Path $Matches[2] $sdkVersion
        }
    }
)
if ($matchingSdkDirectories.Count -ne 1) {
    throw "Expected exactly one installed path for active dotnet SDK '$sdkVersion'; found $($matchingSdkDirectories.Count)."
}

$metadataLoadContextPath = Join-Path $matchingSdkDirectories[0] 'System.Reflection.MetadataLoadContext.dll'
if (-not (Test-Path -LiteralPath $metadataLoadContextPath -PathType Leaf)) {
    throw "The active dotnet SDK does not contain System.Reflection.MetadataLoadContext.dll: $metadataLoadContextPath"
}

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

$assignmentOrder = [System.Collections.Generic.List[object]]::new()
$assignmentOrder.AddRange($testClasses)
$assignmentOrder.Sort([System.Comparison[object]]{
    param($left, $right)

    $weightComparison = $right.StaticCaseWeight.CompareTo($left.StaticCaseWeight)
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
        Classes = [System.Collections.Generic.List[object]]::new()
    })
}

foreach ($testClass in $assignmentOrder) {
    $selectedShard = $mutableShards[0]
    foreach ($candidate in $mutableShards) {
        if ($candidate.StaticCaseWeight -lt $selectedShard.StaticCaseWeight -or
            ($candidate.StaticCaseWeight -eq $selectedShard.StaticCaseWeight -and
             $candidate.Index -lt $selectedShard.Index)) {
            $selectedShard = $candidate
        }
    }

    $selectedShard.Classes.Add($testClass)
    $selectedShard.StaticCaseWeight += $testClass.StaticCaseWeight
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
    TestClasses = @($classCatalog)
    Shards = @($shards)
    SelectedFilter = $shards[$TestShardIndex].Filter
}

$plan | ConvertTo-Json -Depth 8 -Compress
