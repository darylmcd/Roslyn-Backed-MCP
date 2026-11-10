# dotnet-command-runner-unobserved-failure-paths — Make DotnetCommandRunner failure paths observed and its null-logger path emit boundary records

**row:** `dotnet-command-runner-unobserved-failure-paths` · **pri:** `Low` · **size:** `S` · **verified:** `df2f06c1dc495c3e0c4dfaf4ae7e67a54b044efa`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`

## Acceptance

- [ ] A `process.Start()` failure and the stdout/stderr read tasks are observed (awaited or faulted-handled) when `WaitForExitAsync` throws; no unobserved task exception remains.
- [ ] The null-logger constructor paths (used in 13 places) no longer silently skip the kill-failure log and boundary records, or the null-logger constructors are removed in favour of a required logger.
- [ ] The class's folder and namespace agree (`Helpers/` versus `RoslynMcp.Roslyn.Services`).
- [ ] Red-first tests cover each failure path.

## Evidence

- PR 2060 cold review (2026-11-10): `DotnetCommandRunner.cs:~197` skips the kill-failure log when `_logger` is null; `process.Start()` failure and the read tasks are left unobserved when `WaitForExitAsync` throws; folder/namespace mismatch.

## Context

- Pre-existing bad code surfaced while reviewing the boundary-record change. Verify each claim against live code first.
