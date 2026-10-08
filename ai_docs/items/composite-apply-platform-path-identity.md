# composite-apply-platform-path-identity — Preserve physical file identity in composite apply and undo

**row:** `composite-apply-platform-path-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:95`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:194`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Apply filesystem-aware physical path identity throughout composite snapshot capture, successful/partial applied-file projection and undo overlap detection; probe all same-mechanism grouping sites.
- [ ] Observe a Linux case-distinct file regression fail on old code, then pass: both original snapshots are retained, both applied entries remain visible, and revert restores each file independently without false overlap.
- [ ] Preserve Windows insensitive identity and legitimate duplicate-path first-snapshot semantics; verify real apply/revert behavior and partial-failure reporting.

## Evidence

- Reverified during October 7 resume at main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`; no executable reproduction yet.
- CompositeApplyOrchestrator.cs:95: `var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);` drops the second case-distinct physical file's undo snapshot on Linux.
- Lines 167 and 182 project applied paths with `Distinct(StringComparer.OrdinalIgnoreCase)`, collapsing success and partial-failure entries.
- UndoService.cs:194: `var targetFiles = new HashSet<string>(target.AffectedFiles, StringComparer.OrdinalIgnoreCase);` uses the same identity mistake for revert overlap decisions.

## Context

- Independent of fork snapshot lookup and preview completeness metadata. Include it in any implementation that directly exposes this path grouping; otherwise retain focused tracked repair.
