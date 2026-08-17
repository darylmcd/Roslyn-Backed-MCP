param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts",
    [switch]$NoCoverage,
    [switch]$ExcludeNetworkTests,
    [switch]$RequireConsumedFragments
)

$ErrorActionPreference = "Stop"

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

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "RoslynMcp.slnx"
$sampleSolutionPath = Join-Path $repoRoot "samples\SampleSolution\SampleSolution.slnx"
$hostProject = Join-Path $repoRoot "src\RoslynMcp.Host.Stdio\RoslynMcp.Host.Stdio.csproj"
$publishDir = Join-Path $repoRoot "$OutputRoot\publish\host-stdio"
$manifestDir = Join-Path $repoRoot "$OutputRoot\manifests"
$coverageDir = Join-Path $repoRoot "$OutputRoot\coverage"
$hashManifestPath = Join-Path $manifestDir "host-stdio-sha256.txt"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
if (-not $NoCoverage) {
    New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
}

# Version-string drift check across all six version files.
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

# Registry install-readiness scorecard — first slice of BRAIN-002.
# Validates .claude-plugin/server.json against MCP-registry expectations,
# cross-checks plugin.json + marketplace.json, and emits a structured
# artifact at artifacts/registry-readiness.json that /publish-preflight reads.
Invoke-ChildScriptStep `
    -Description 'Registry readiness validation' `
    -ScriptPath (Join-Path $PSScriptRoot 'verify-registry-readiness.ps1') `
    -Parameters @{ Quiet = $true }

dotnet restore $solutionPath --nologo
Invoke-DotnetStep "dotnet restore (main solution)"

# Sample solution restore: integration tests load samples/SampleSolution/SampleSolution.slnx
# via MSBuildWorkspace and then run CompileCheckService. That project tree references
# MSTest (for SampleLib.Tests) and the packages must be resolved in the NuGet global-packages
# cache before the workspace compiles — otherwise the sample tests project emits CS0234/CS0246
# for Microsoft.VisualStudio.TestTools and the ExtractMethod integration tests fail.
dotnet restore $sampleSolutionPath --nologo
Invoke-DotnetStep "dotnet restore (sample solution)"

# The other two sample fixtures were never pre-restored, so the integration tests that
# spawn `dotnet build` against them (SemanticExpansionTests, ValidationIntegrationTests)
# paid a cold implicit restore INSIDE the timed child command — on a CI service account
# with a cold NuGet cache that restore dominated the command timeout. Restore them up
# front like SampleSolution so the in-test builds only compile.
$generatedDocSolutionPath = Join-Path $repoRoot "samples\GeneratedDocumentSolution\GeneratedDocumentSolution.slnx"
$buildFailureSolutionPath = Join-Path $repoRoot "samples\BuildFailureSolution\BuildFailureSolution.slnx"
dotnet restore $generatedDocSolutionPath --nologo
Invoke-DotnetStep "dotnet restore (generated-document solution)"
dotnet restore $buildFailureSolutionPath --nologo
Invoke-DotnetStep "dotnet restore (build-failure solution)"

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
    # PR CI gates vulnerabilities via the dedicated 'Audit packages' workflow step, so the
    # live api.nuget.org integration tests are redundant there and network-flaky on the
    # self-hosted runner. workflow_dispatch and the weekly schedule stay unfiltered so the
    # live scan still runs as a canary on hosted runners.
    $testFilter += "&TestCategory!=Network"
}

# Microsoft.CodeAnalysis.Testing extracts ReferenceAssemblies packages beneath
# Path.GetTempPath()/test-packages. A long-lived self-hosted runner therefore carried a
# partially-extracted package from one CI job into the next. Give each testhost invocation a
# private temp root so extraction state has the same lifetime as this gate, while leaving the
# parent PowerShell process's TEMP/TMP and every other concurrent gate untouched.
$testTempRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "RoslynMcpTestRuns\$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testTempRoot -Force | Out-Null
Write-Host "Testhost temp root: $testTempRoot"
$testFailure = $null
try {
    $testEnvironment = @(
        "--environment", "TEMP=$testTempRoot",
        "--environment", "TMP=$testTempRoot"
    )
    if ($NoCoverage) {
        dotnet test $solutionPath -c $Configuration --no-build --nologo `
            --filter $testFilter `
            --logger "console;verbosity=minimal" `
            @testEnvironment
    } else {
        dotnet test $solutionPath -c $Configuration --no-build --nologo `
            --filter $testFilter `
            --collect:"XPlat Code Coverage" `
            --results-directory $coverageDir `
            --logger "console;verbosity=minimal" `
            @testEnvironment
    }
    Invoke-DotnetStep "dotnet test"
}
catch {
    $testFailure = $_
}

$cleanupFailures = [System.Collections.Generic.List[System.Exception]]::new()

# Child builds can leave VBCSCompiler/MSBuild servers holding files below the private temp
# root. This is the repository's sanctioned Windows lock-release step; the runner executes
# one validation job at a time, and hosted runners are single-job ephemeral machines.
try {
    dotnet build-server shutdown
    Invoke-DotnetStep "dotnet build-server shutdown"
}
catch {
    $cleanupFailures.Add($_.Exception)
}

try {
    Remove-PrivateTestTempRoot -Path $testTempRoot
}
catch {
    $cleanupFailures.Add($_.Exception)
}

if ($testFailure -and $cleanupFailures.Count -gt 0) {
    $allFailures = [System.Collections.Generic.List[System.Exception]]::new()
    $allFailures.Add($testFailure.Exception)
    $allFailures.AddRange($cleanupFailures)
    throw [System.AggregateException]::new(
        'dotnet test failed and test-environment cleanup also failed.',
        $allFailures)
}

if ($testFailure) {
    throw $testFailure
}

if ($cleanupFailures.Count -gt 0) {
    throw [System.AggregateException]::new(
        'Test-environment cleanup failed.',
        $cleanupFailures)
}

# PublishReadyToRun (CrossGen) can fail on CI runners when the SDK's crossgen2
# tooling has platform-specific issues. Disable for the verification publish step;
# the NuGet pack step produces the distributable package independently.
dotnet publish $hostProject -c $Configuration --no-build -o $publishDir -p:PublishReadyToRun=false
Invoke-DotnetStep "dotnet publish"

$hashLines = Get-ChildItem -Path $publishDir -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $relativePath = Resolve-Path -Relative $_.FullName
        "$hash  $relativePath"
    }

Set-Content -Path $hashManifestPath -Value $hashLines

Write-Host "Publish directory: $publishDir"
Write-Host "Hash manifest: $hashManifestPath"
if ($NoCoverage) {
    Write-Host "Code coverage: skipped (-NoCoverage)"
} else {
    Write-Host "Code coverage (Cobertura): $coverageDir"
}
