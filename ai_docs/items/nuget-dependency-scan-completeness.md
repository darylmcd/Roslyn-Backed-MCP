# nuget-dependency-scan-completeness — Report NuGet dependency scan completeness

**row:** `nuget-dependency-scan-completeness` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Core/Models/NuGetDependencyDto.cs`
- `src/RoslynMcp.Core/Services/INuGetDependencyService.cs`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — `get_nuget_dependencies`.
- `tests/RoslynMcp.Tests/NuGetDependencySummaryTests.cs`

## Acceptance

- [ ] Add a detailed scan result/method carrying `NuGetDependencyResultDto`, `IsComplete`, and `FailedProjectCount`; both summary/full tool response modes expose the completeness fields and document totals as observed lower bounds.
- [ ] Make existing `GetNuGetDependenciesAsync` a compatibility projection over the detailed scan that fails closed with stable safe guidance when incomplete, so prompt/migration consumers never infer package absence from partial data.
- [ ] Preserve successful projects/packages after a per-project evaluation failure, increment the failure count, and never present a failed project as a trustworthy empty dependency set.
- [ ] Emit only secret-safe correlated diagnostics for failed evaluation; no raw project path, exception message, or secret-bearing value reaches the response or sink.
- [ ] Replace the silent cancellation break with `ThrowIfCancellationRequested` so cancellation never returns partial success.
- [ ] One table-driven scanner-outcome regression covers complete, mixed-success/failure, and canceled scans, including partial detailed output, compatibility-projection refusal, and unchanged complete ordering/summary/full semantics.
- [ ] Classify the additive public fields under the SDK compatibility decision and changelog policy.

## Evidence

- `NuGetDependencyService` catches per-project evaluation failures, logs them, and still adds an empty-package project to results whose totals appear complete.
- Prompt and guided-migration consumers call the same list-returning interface and can turn that partial result into a false “no projects reference this package” conclusion.
- Its cancellation poll breaks the scan and returns a partial result instead of propagating cancellation.
