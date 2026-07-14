param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts",
    [switch]$NoCoverage,
    [switch]$ExcludeNetworkTests
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

# Version-string drift check across all 5 version files.
# Runs before build so a drift-only mistake fails fast without waiting for compilation.
& (Join-Path $PSScriptRoot 'verify-version-drift.ps1')

# Shipped-skill generality check — blocks a publish that carries repo-only
# references in ./skills/ (repo-only skills belong in .claude/skills/).
& (Join-Path $PSScriptRoot 'verify-skills-are-generic.ps1')

# Plugin package allowlist check — ensures the Claude Code plugin cache sync
# cannot ship repo-internal source, tests, ai_docs, or release infrastructure.
& (Join-Path $PSScriptRoot 'verify-plugin-package-files.ps1')

# Changelog fragment format check — catches malformed changelog.d/*.md files
# at PR time rather than at release-cut time (where they block /bump).
& (Join-Path $PSScriptRoot 'verify-changelog-fragments.ps1')

# Registry install-readiness scorecard — first slice of BRAIN-002.
# Validates .claude-plugin/server.json against MCP-registry expectations,
# cross-checks plugin.json + marketplace.json, and emits a structured
# artifact at artifacts/registry-readiness.json that /publish-preflight reads.
& (Join-Path $PSScriptRoot 'verify-registry-readiness.ps1') -Quiet

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
if ($NoCoverage) {
    dotnet test $solutionPath -c $Configuration --no-build --nologo `
        --filter $testFilter `
        --logger "console;verbosity=minimal"
} else {
    dotnet test $solutionPath -c $Configuration --no-build --nologo `
        --filter $testFilter `
        --collect:"XPlat Code Coverage" `
        --results-directory $coverageDir `
        --logger "console;verbosity=minimal"
}
Invoke-DotnetStep "dotnet test"

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
