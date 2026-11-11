# test-run-no-build — Run previously built test binaries

**row:** `test-run-no-build` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Core/Services/ITestRunnerService.cs`

## Acceptance

- [ ] Opt-in `test_run noBuild=true` runs the previously built tests without building or restoring, for both VSTest and native MTP; translated MTP filters must be safe against the selected built test artifacts, including OR support. The tool argument description states that it runs the last built binaries. Omitted or false keeps existing execution and wire behavior. Cover the opt-in raw-wire argument, real existing binaries, no rebuild/restore, and absent or unsuitable binaries with causal tests.

## Evidence

- Base 52f2c418: TestRunnerService.cs:88,162,399,446 plans and invokes tests without a no-build option; native MTP planning explicitly restores for OR filters at :346-351.

## Context

- Additive stable request option; 5.x minor. Preserve the existing results schema and default. Update owned callers and doubles without a fallback that discards noBuild. The separately actionable MSBuild property option remains in test-run-nobuild-msbuild-properties.

## Notes

2026-11-11 eligibility: the stable request addition can fit a 5.x minor. Leave unselected until the native MTP filtered plan proves the executed module set, built TUnit.Engine versions and exact test identities, including custom launch and target frameworks. Current source and project.assets.json can differ from last built outputs. SDK 10 ListTestsOption and TerminalTestReporter publish display names, and TUnit 1.46 TestDiscoveryService bypasses filter-aware discovery when isForExecution=false; list-tests cannot certify the OR execution path. Do not ship VSTest-only or bypass the existing safety checks. No runtime probe of an alternative identity source has established completeness yet.
