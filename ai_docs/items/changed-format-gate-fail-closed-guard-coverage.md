# changed-format-gate-fail-closed-guard-coverage — cover the changed-file formatter gate's fail-closed guards

**row:** `changed-format-gate-fail-closed-guard-coverage` · **pri:** `Medium` · **size:** `S` · **deps:** `changed-format-gate-diagnostic-id-contract`

## Anchors

- `eng/verify-changed-format.ps1:143`
- `eng/verify-changed-format.ps1:197`
- `eng/verify-changed-format.ps1:201`
- `tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs`

## Acceptance

- [ ] A test asserts a non-existent `-BaseRef` exits non-zero with the fetch-the-base-ref guidance.
- [ ] A test asserts a format report containing the truncation marker fails rather than passing on an empty report.
- [ ] A test asserts an unexpected `dotnet format` exit code fails closed.

## Evidence

Cold review of PR #1356 read all seven test methods in `ChangedFormatGateScriptTests.cs`: they cover no-op, clean, IMPORTS, IDE1006, FINALNEWLINE, tracked-debt, and baseline+1. None exercises the three throw sites the script's own "Fail-closed contract" section calls out.

## Context

Surfaced by cold code-quality review of `format-changed-file-gate` (PR #1356, sweep `20260825T151721Z`). The gate is a CI guard, so its own failure modes silently degrading to a pass is the highest-consequence regression it can have.
