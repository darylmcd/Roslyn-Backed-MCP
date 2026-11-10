# undo-overlapping-captures-single-slot — Decide the single pending-capture slot invariant

**row:** `undo-overlapping-captures-single-slot` · **pri:** `Low` · **size:** `M` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:91` — CaptureBeforeApply overwrites the one _snapshots entry per workspace.
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:134` — writers serialized by gate.RunWriteAsync (also :438, :506).
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Decide with evidence: keep the invariant explicit (assertion or doc plus a test of the gate guarantee), or keep per-capture pending state so a later capture cannot supersede an uncommitted/failed one.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): a later capture B supersedes an uncommitted/failed capture A, losing A's retryable undo; overlap cannot happen in production today because of the write gate.
