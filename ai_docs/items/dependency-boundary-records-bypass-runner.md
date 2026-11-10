# dependency-boundary-records-bypass-runner — Add duration and outcome records for subprocess and HTTP boundaries that bypass DotnetCommandRunner

**row:** `dependency-boundary-records-bypass-runner` · **pri:** `Low` · **size:** `M` · **verified:** `df2f06c1dc495c3e0c4dfaf4ae7e67a54b044efa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GitChangedFilesCollector.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptWorkerProcess.cs`
- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs`

## Acceptance

- [ ] Each of the three boundaries (git subprocess, script worker process, NuGet version HTTP call) emits one boundary record with outcome and durationMs, using the allowlist-only field rules of `CommandBoundaryTelemetry` (no arguments, paths, filter text or URLs) and a new `LogEvents` id in the matching range.
- [ ] Red-first tests per boundary cover success, failure and cancellation; an allowlist test pins the field set.
- [ ] Docs list the new records beside the existing boundary records in `docs/stdio-client-integration.md`.

## Evidence

- PR 2060 cold review (2026-11-10): `DotnetCommandRunner` now owns the dotnet boundary record, but `GitChangedFilesCollector.cs:86` (git), `ScriptWorkerProcess.cs:46` and `NuGetVersionChecker` (HTTP) bypass it and log no duration or outcome. `logging-event-ids-remaining-call-sites` covers ids only.

## Context

- Additive log records only; patch/minor. Verify every site against live code first.
