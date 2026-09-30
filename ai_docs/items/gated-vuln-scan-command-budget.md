# gated-vuln-scan-command-budget — Bound nuget_vulnerability_scan by VulnerabilityScanTimeout, not the 2-minute gate timeout

**row:** `gated-vuln-scan-command-budget` · **pri:** `High` · **size:** `M` · **deps:** `gated-build-command-budget`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:60-80`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:235-260`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:73-101`
- (new) `tests/RoslynMcp.Tests/GateOperationDeadlineTests.cs`

## Acceptance

- [ ] `nuget_vulnerability_scan` is bounded by `VulnerabilityScanTimeout` (5 minutes, `ValidationServiceOptions.cs:38`), not the 2-minute gate `RequestTimeout`: it runs under `gate.RunReadAsync` at `SecurityTools.cs:73` today; apply the release-then-execute pattern from `gated-build-command-budget` so the command phase holds no workspace lock or throttle slot.
- [ ] Red-first test: a scan command that runs 3 minutes completes; a command outliving `VulnerabilityScanTimeout` keeps category `Timeout`.
- [ ] Additive fields only; stable `nuget_vulnerability_scan` keeps its shipped outcomes (only which timer fires changes).

## Evidence

- Retro 2026-09-27 (see parent): gated command calls time out at about 120 s; `GatedCommandExecutor.cs:74-75` already arms the scan's own budget inside the outer gate, so the request deadline fires first.

## Context

- Seam 4 of split parent `gated-build-test-operation-deadline`; reuses the pattern of `gated-build-command-budget`.
