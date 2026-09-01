param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts",
    [switch]$NoCoverage,
    [switch]$ExcludeNetworkTests,
    [switch]$RequireConsumedFragments,
    [switch]$TestShardOnly,
    [int]$TestShardIndex = 0,
    [int]$TestShardCount = 1
)

$ErrorActionPreference = "Stop"

if ($TestShardCount -lt 1 -or $TestShardCount -gt 16) {
    throw "TestShardCount must be between 1 and 16; received $TestShardCount."
}
if ($TestShardIndex -lt 0 -or $TestShardIndex -ge $TestShardCount) {
    throw "TestShardIndex must be between 0 and $($TestShardCount - 1); received $TestShardIndex."
}
if ($TestShardOnly -and $RequireConsumedFragments) {
    throw 'TestShardOnly cannot be combined with RequireConsumedFragments.'
}

# PowerShell does not honor $ErrorActionPreference = "Stop" for native commands.
# Every dotnet invocation below must call this after returning.
function Invoke-DotnetStep {
    param([string]$Description)
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

# Invoking a child PowerShell script with `&` does not make a nonzero `exit`
# terminate this parent script. Reset the global native-exit state because some
# successful validators return without an explicit `exit 0`, then capture it
# immediately after the child returns so a later command cannot overwrite it.
# Terminating PowerShell errors are deliberately not caught and continue to
# propagate under $ErrorActionPreference = "Stop".
function Invoke-ChildScriptStep {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string]$ScriptPath,

        [hashtable]$Parameters = @{}
    )

    $global:LASTEXITCODE = 0
    & $ScriptPath @Parameters
    $exitCode = $global:LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

# Windows can retain a testhost/collector handle for a short interval after
# `dotnet test` exits. Only retry the sharing/access failures that can clear;
# every other cleanup error remains immediately fatal.
function Remove-PrivateTestTempRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $canonicalPath = [System.IO.Path]::GetFullPath($Path)
    $testRunParent = [System.IO.Path]::GetFullPath(
        (Join-Path ([System.IO.Path]::GetTempPath()) 'RoslynMcpTestRuns'))
    $relativePath = [System.IO.Path]::GetRelativePath($testRunParent, $canonicalPath)
    if ($relativePath -notmatch '^[0-9a-fA-F]{32}$') {
        throw 'Refusing to recursively remove a path outside the private test-run directory contract.'
    }

    $delayMilliseconds = 100
    $maxAttempts = 8
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $canonicalPath -PathType Container)) {
                return
            }

            Remove-Item -LiteralPath $canonicalPath -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $isTransient =
                $_.Exception -is [System.IO.IOException] -or
                $_.Exception -is [System.UnauthorizedAccessException]
            if (-not $isTransient) {
                throw
            }

            if ($attempt -eq $maxAttempts) {
                throw "Private test-temp cleanup remained blocked after $maxAttempts attempts."
            }

            Write-Warning "Private test-temp cleanup was temporarily blocked (attempt $attempt of $maxAttempts); retrying."
            Start-Sleep -Milliseconds $delayMilliseconds
            $delayMilliseconds = [Math]::Min($delayMilliseconds * 2, 1000)
        }
    }
}

function Resolve-DescendantPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $canonicalRoot = [System.IO.Path]::GetFullPath($Root)
    $canonicalPath = [System.IO.Path]::GetFullPath($Path)
    $relativePath = [System.IO.Path]::GetRelativePath($canonicalRoot, $canonicalPath)
    $escapesRoot =
        [System.IO.Path]::IsPathRooted($relativePath) -or
        $relativePath -eq '..' -or
        $relativePath.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal) -or
        $relativePath.StartsWith(
            "..$([System.IO.Path]::AltDirectorySeparatorChar)",
            [System.StringComparison]::Ordinal)
    if ($relativePath -eq '.' -or $escapesRoot) {
        throw "$Description must be an exact descendant of its repository-owned boundary."
    }

    return $canonicalPath
}

function Reset-VerifierOwnedDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$OutputBoundary,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $canonicalPath = Resolve-DescendantPath `
        -Root $OutputBoundary `
        -Path $Path `
        -Description $Description
    if (Test-Path -LiteralPath $canonicalPath) {
        Remove-Item -LiteralPath $canonicalPath -Recurse -Force -ErrorAction Stop
    }
    New-Item -ItemType Directory -Path $canonicalPath -Force | Out-Null
}

function Remove-VerifierOwnedDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$OutputBoundary,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $canonicalPath = Resolve-DescendantPath `
        -Root $OutputBoundary `
        -Path $Path `
        -Description $Description
    if (Test-Path -LiteralPath $canonicalPath) {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath $canonicalPath `
            -Recurse `
            -Force `
            -ErrorAction Stop
    }
}

function Assert-TestResultFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        [System.IO.FileInfo]::new($Path).Length -eq 0) {
        throw "dotnet test succeeded without producing the required TRX result: $Path"
    }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($Path, $settings)
        $document.Load($reader)
    }
    catch {
        throw "dotnet test produced an unreadable TRX result: $Path"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }

    if ($null -eq $document.DocumentElement -or
        $document.DocumentElement.LocalName -ne 'TestRun') {
        throw "dotnet test produced XML that is not an MSTest TRX result: $Path"
    }

    $counters = $document.SelectSingleNode(
        "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    $total = 0
    $executed = 0
    if ($null -eq $counters -or
        -not [int]::TryParse($counters.Attributes['total']?.Value, [ref]$total) -or
        -not [int]::TryParse($counters.Attributes['executed']?.Value, [ref]$executed) -or
        $total -lt 1 -or
        $executed -lt 1) {
        throw "dotnet test produced a TRX result that does not prove any tests executed: $Path"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "RoslynMcp.slnx"
$testProject = Join-Path $repoRoot "tests\RoslynMcp.Tests\RoslynMcp.Tests.csproj"
$hostProject = Join-Path $repoRoot "src\RoslynMcp.Host.Stdio\RoslynMcp.Host.Stdio.csproj"
$requestedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot
} else {
    Join-Path $repoRoot $OutputRoot
}
$outputRootPath = Resolve-DescendantPath `
    -Root $repoRoot `
    -Path $requestedOutputRoot `
    -Description 'OutputRoot'
$publishDir = Join-Path $outputRootPath "publish\host-stdio"
$manifestDir = Join-Path $outputRootPath "manifests"
$coverageDir = Join-Path $outputRootPath "coverage"
$testResultsDir = Join-Path $outputRootPath "test-results"
$runSettingsPath = Join-Path $PSScriptRoot 'ci.runsettings'
$hashManifestPath = Join-Path $manifestDir "host-stdio-sha256.txt"

New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
if (-not $TestShardOnly) {
    Reset-VerifierOwnedDirectory `
        -OutputBoundary $outputRootPath `
        -Path $publishDir `
        -Description 'Publish directory'
    Reset-VerifierOwnedDirectory `
        -OutputBoundary $outputRootPath `
        -Path $manifestDir `
        -Description 'Manifest directory'
}
if (-not $NoCoverage) {
    New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
}

$primaryFailure = $null
$cleanupFailures = [System.Collections.Generic.List[System.Exception]]::new()
$testTempRoot = $null
try {
if (-not $TestShardOnly) {
    # Coordinated package families fail before restore so a split Dependabot update cannot
    # publish artifacts or reach MSBuildLocator's later runtime-asset failure.
    Invoke-ChildScriptStep `
        -Description 'Package family parity validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-package-family-parity.ps1')

    # Version-string drift check across all seven version files.
    # Runs before build so a drift-only mistake fails fast without waiting for compilation.
    Invoke-ChildScriptStep `
        -Description 'Version drift validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-version-drift.ps1')

    # Shipped-skill generality check — blocks a publish that carries repo-only
    # references in ./skills/ (repo-only skills belong in .claude/skills/).
    Invoke-ChildScriptStep `
        -Description 'Shipped skill validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-skills-are-generic.ps1')

    # Plugin package allowlist check — ensures the Claude Code plugin cache sync
    # cannot ship repo-internal source, tests, ai_docs, or release infrastructure.
    Invoke-ChildScriptStep `
        -Description 'Plugin package allowlist validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-plugin-package-files.ps1')

    # Changelog fragment format check — catches malformed changelog.d/*.md files
    # at PR time rather than at release-cut time (where they block /bump).
    Invoke-ChildScriptStep `
        -Description 'Changelog fragment validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-changelog-fragments.ps1')

    # Breaking-version policy check — pending breaking fragments may remain on an
    # implementation branch, but a consumed top release section must advance the major.
    if ($RequireConsumedFragments) {
        Invoke-ChildScriptStep `
            -Description 'Breaking version validation' `
            -ScriptPath (Join-Path $PSScriptRoot 'verify-breaking-version-bump.ps1') `
            -Parameters @{ RequireConsumedFragments = $true }
    }
    else {
        Invoke-ChildScriptStep `
            -Description 'Breaking version validation' `
            -ScriptPath (Join-Path $PSScriptRoot 'verify-breaking-version-bump.ps1')
    }

    # Registry install-readiness scorecard validates the published plugin and
    # marketplace metadata and emits the artifact consumed by publish preflight.
    Invoke-ChildScriptStep `
        -Description 'Registry readiness validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-registry-readiness.ps1') `
        -Parameters @{ Quiet = $true }
}

dotnet restore $solutionPath --nologo
Invoke-DotnetStep "dotnet restore (main solution)"

if (-not $TestShardOnly) {
    Invoke-ChildScriptStep `
        -Description 'Restored third-party license validation' `
        -ScriptPath (Join-Path $PSScriptRoot 'update-third-party-notices.ps1') `
        -Parameters @{ RepoRoot = $repoRoot; Verify = $true; VerifyRestoredLicenses = $true }
}

Invoke-ChildScriptStep `
    -Description 'Test fixture preparation' `
    -ScriptPath (Join-Path $PSScriptRoot 'prepare-test-fixtures.ps1') `
    -Parameters @{ RepoRoot = $repoRoot }

dotnet build $solutionPath -c $Configuration --no-restore --nologo
Invoke-DotnetStep "dotnet build"

# Logger verbosity `minimal` emits the run summary and failure details but skips
# the per-test "Passed X [N ms]" lines that dominated the previous console output.
# Coverage collection adds coverlet IL-rewrite latency per test assembly (~60-90s total).
# CI_POLICY.md treats coverage as informational — not a merge gate — so PR-time collection
# is pure latency. `-NoCoverage` lets CI skip it on pull_request while workflow_dispatch
# and the weekly schedule still collect for the uploaded artifact.
#
# --filter "TestCategory!=Benchmark" excludes the WorkspaceReadConcurrencyBenchmark
# test, which measures wall-clock RW-lock behavior. Its docstring declares it
# opt-in via `dotnet test --filter "TestCategory=Benchmark"` — this filter aligns
# the default invocation with that contract.
$testFilter = "TestCategory!=Benchmark"
if ($ExcludeNetworkTests) {
    # PR CI retains the fail-closed package audit while excluding every test that depends on
    # live network state, including package-audit and restore canaries. workflow_dispatch and
    # the weekly schedule stay unfiltered so those integration contracts run on hosted runners.
    $testFilter += "&TestCategory!=Network"
}

# A shard selects whole test classes, preserving MSTest's class-level lifecycle
# contract while allowing independent CI jobs to run disjoint halves. The
# unsharded default intentionally avoids a redundant all-class filter.
$testRunId = "{0}-shard-{1}-of-{2}-{3}" -f @(
    ($Configuration -replace '[^A-Za-z0-9_.-]', '_'),
    ($TestShardIndex + 1),
    $TestShardCount,
    [Guid]::NewGuid().ToString('N'))
$trxPath = Join-Path $testResultsDir "$testRunId.trx"
if ($TestShardCount -gt 1) {
    $targetPathArguments = @(
        'msbuild',
        $testProject,
        '--nologo',
        '-getProperty:TargetPath',
        "-property:Configuration=$Configuration")
    $global:LASTEXITCODE = 0
    $targetPathOutput = @(& dotnet @targetPathArguments)
    Invoke-DotnetStep "dotnet msbuild (resolve test assembly)"
    $testAssemblyPath = ($targetPathOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($testAssemblyPath)) {
        throw 'dotnet msbuild returned no test assembly path.'
    }

    $planParameters = @{
        TestAssemblyPath = $testAssemblyPath
        TestShardCount = $TestShardCount
        TestShardIndex = $TestShardIndex
    }
    $planJson = & (Join-Path $PSScriptRoot 'get-test-shard-plan.ps1') @planParameters
    $testShardPlan = $planJson | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($testShardPlan.SelectedFilter)) {
        throw "Test shard $TestShardIndex produced an empty class filter."
    }

    $testFilter = "($($testShardPlan.SelectedFilter))&$testFilter"
    $planPath = Join-Path $testResultsDir "$testRunId-plan.json"
    $planJson | Set-Content -LiteralPath $planPath -Encoding utf8
    Write-Host ("Test shard: {0}/{1} ({2} classes; static case weight {3})" -f @(
        ($TestShardIndex + 1),
        $TestShardCount,
        $testShardPlan.Shards[$TestShardIndex].ClassCount,
        $testShardPlan.Shards[$TestShardIndex].StaticCaseWeight))
    Write-Host "Test shard plan: $planPath"
}

# Microsoft.CodeAnalysis.Testing extracts ReferenceAssemblies packages beneath
# Path.GetTempPath()/test-packages. A reused developer or agent machine can carry a
# partially-extracted package into the next run. Give each testhost invocation a private temp
# root so extraction state has the same lifetime as this gate, while leaving the parent
# PowerShell process environment and every other concurrent gate untouched.
$testTempRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "RoslynMcpTestRuns\$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testTempRoot -Force | Out-Null
Write-Host "Testhost temp root: $testTempRoot"
$testEnvironment = @(
    "--environment", "TEMP=$testTempRoot",
    "--environment", "TMP=$testTempRoot",
    "--environment", "TMPDIR=$testTempRoot"
)
$testResultsOutputDirectory = if ($NoCoverage) { $testResultsDir } else { $coverageDir }
$testArguments = @(
    'test',
    $solutionPath,
    '-c', $Configuration,
    '--no-build',
    '--nologo',
    '-p:TestFixturesPrepared=true',
    '--filter', $testFilter,
    '--settings', $runSettingsPath,
    '--results-directory', $testResultsOutputDirectory,
    '--logger', 'console;verbosity=minimal',
    '--logger', "trx;LogFileName=$trxPath")
if (-not $NoCoverage) {
    $testArguments += '--collect:XPlat Code Coverage'
}
$testArguments += $testEnvironment

& dotnet @testArguments
Invoke-DotnetStep "dotnet test"
Assert-TestResultFile -Path $trxPath

if (-not $TestShardOnly) {
    # PublishReadyToRun (CrossGen) can fail on CI runners when the SDK's crossgen2
    # tooling has platform-specific issues. Disable for the verification publish step;
    # the NuGet pack step produces the distributable package independently.
    dotnet publish $hostProject -c $Configuration --no-build -o $publishDir -p:PublishReadyToRun=false
    Invoke-DotnetStep "dotnet publish"

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishDir -File -Recurse)
    if ($publishedFiles.Count -eq 0) {
        throw 'dotnet publish succeeded without producing any files.'
    }

    $hashLines = @($publishedFiles |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $relativePath = Resolve-Path -Relative $_.FullName
            "$hash  $relativePath"
        })
    if ($hashLines.Count -eq 0) {
        throw 'Release publish output produced an empty SHA-256 manifest.'
    }

    Set-Content -LiteralPath $hashManifestPath -Value $hashLines

    Write-Host "Publish directory: $publishDir"
    Write-Host "Hash manifest: $hashManifestPath"
}
Write-Host "Test results (TRX): $trxPath"
if ($NoCoverage) {
    Write-Host "Code coverage: skipped (-NoCoverage)"
} else {
    Write-Host "Code coverage (Cobertura): $coverageDir"
}
}
catch {
    $primaryFailure = $_.Exception
}
finally {
    # Child builds can leave VBCSCompiler/MSBuild servers holding verifier-owned state.
    # Attempt both cleanup actions exactly once, even when restore/build/planning/test/publish fails.
    try {
        dotnet build-server shutdown
        Invoke-DotnetStep "dotnet build-server shutdown"
    }
    catch {
        $cleanupFailures.Add($_.Exception)
    }

    if ($null -ne $testTempRoot) {
        try {
            Remove-PrivateTestTempRoot -Path $testTempRoot
        }
        catch {
            $cleanupFailures.Add($_.Exception)
        }
    }
}

if ($null -ne $primaryFailure -or $cleanupFailures.Count -gt 0) {
    foreach ($ownedOutput in @(
        @{ Path = $publishDir; Description = 'Publish directory' },
        @{ Path = $manifestDir; Description = 'Manifest directory' })) {
        try {
            Remove-VerifierOwnedDirectory `
                -OutputBoundary $outputRootPath `
                -Path $ownedOutput.Path `
                -Description $ownedOutput.Description
        }
        catch {
            $cleanupFailures.Add($_.Exception)
        }
    }
}

if ($null -ne $primaryFailure -and $cleanupFailures.Count -gt 0) {
    $allFailures = [System.Collections.Generic.List[System.Exception]]::new()
    $allFailures.Add($primaryFailure)
    $allFailures.AddRange($cleanupFailures)
    throw [System.AggregateException]::new(
        'Release verification failed and verifier-owned cleanup also failed.',
        $allFailures)
}

if ($null -ne $primaryFailure) {
    throw $primaryFailure
}

if ($cleanupFailures.Count -gt 0) {
    throw [System.AggregateException]::new(
        'Verifier-owned cleanup failed.',
        $cleanupFailures)
}
