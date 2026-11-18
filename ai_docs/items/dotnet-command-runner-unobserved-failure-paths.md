# dotnet-command-runner-unobserved-failure-paths — Make DotnetCommandRunner failure paths observed and its null-logger path emit boundary records

**row:** `dotnet-command-runner-unobserved-failure-paths` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`

## Acceptance

- [ ] Observe start failures and every acquired stdout/stderr reader task when process exit waiting fails. Preserve the primary failure and cleanup diagnostics.
- [ ] Dispose each acquired stdout and stderr reader after its task settles. Exercise success, caller cancellation, early termination and partial acquisition. Prove the underlying handles close.
- [ ] The null-logger constructor paths (used in 13 places) no longer silently skip the kill-failure log and boundary records, or the null-logger constructors are removed in favour of a required logger.
- [ ] The class's folder and namespace agree (`Helpers/` versus `RoslynMcp.Roslyn.Services`).
- [ ] Red-first tests cover each failure path.

## Evidence

- PR 2060 cold review (2026-11-10): `DotnetCommandRunner.cs:~197` skips the kill-failure log when `_logger` is null; `process.Start()` failure and the read tasks are left unobserved when `WaitForExitAsync` throws; folder/namespace mismatch.

## Context

- Pre-existing bad code surfaced while reviewing the boundary-record change. Verify each claim against live code first.
