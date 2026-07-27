# workspace-fork-apply-orchestration-decomposition — Decompose fork-apply orchestration

**row:** `workspace-fork-apply-orchestration-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs` (`ApplyAsync`)
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`

## Acceptance

- [ ] Separate fork lifecycle, validation execution, and retention cleanup into named helpers with one owner per state transition.
- [ ] Reduce `ApplyAsync` below 80 executable lines and cyclomatic complexity below 10.
- [ ] Preserve cleanup-on-exception and all four retention policies with focused regressions.

## Evidence

- Live Roslyn complexity metrics on 2026-07-26 reported a 106-line, complexity-13 `ApplyAsync` after the Host extraction.
