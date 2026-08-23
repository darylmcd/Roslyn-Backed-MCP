# test-runner-mtp-filter-discovery-fail-closed — Require discovery-backed MTP filter validation

**row:** `test-runner-mtp-filter-discovery-fail-closed` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs` — nullable `ITestDiscoveryService` dependency and conditional known-test lookup
- `src/RoslynMcp.Host.Stdio/Program.cs` — production registration and constructor wiring
- `tests/RoslynMcp.Tests/TUnitMtpNativeTestRunTests.cs` — direct-construction MTP filter regressions

## Acceptance

- [ ] Remove the fail-open path that translates a TUnit `FullyQualifiedName~` filter without discovery-backed known-test validation.
- [ ] Keep production dependency injection and direct service construction explicit and fail closed when discovery is unavailable.
- [ ] Add one regression proving an ambiguous class-like contains filter cannot bypass validation through direct construction.

## Evidence

- PR #1315 review found that production DI supplies `ITestDiscoveryService`, but the public constructor accepts `null` and `ResolveMtpNativeExecutionPlanAsync` silently skips known-test validation in that case.
