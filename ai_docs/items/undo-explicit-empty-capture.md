# undo-explicit-empty-capture — Represent an undo operation with no effects

**row:** `undo-explicit-empty-capture` · **pri:** `Medium` · **size:** `S` · **deps:** `restore-physical-project-mutation-rendering`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:118`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:240`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] An explicit empty file capture with no solution snapshot represents an operation with no effects; distinguish absent file capture and existing nonempty or solution-backed captures.
- [ ] Revert both latest and by-sequence empty captures without touching files, workspace state/version or earlier/later real apply history; consume once and honor cancellation.
- [ ] Reproduce old behavior with real A, empty B and later C; undo B must preserve A/C disk/workspace contents and leave real history undoable.
- [ ] Reuse one snapshot dispatch rule for both undo entry points; preserve existing rollback, dependency and failed/cancelled retry contracts.
- [ ] Keep one production file plus focused tests and fragment; observe RED/GREEN, fresh review and required hosted gates, verify merge and full delivery gate before closure.

## Evidence

- PR1811 held draft at named third implementation-review-round stop trigger. Its no-op success requires a no-effect undo capture; whole-solution captures with empty affected-file sets can erase later edits because dependency checking sees no overlapping files.
- Current UndoService selects file restoration only for Count>0. Null solution plus explicit empty file list falls back to invalid whole-solution restoration instead of a no-effect operation.
- Parent rebase correction remains one production file; land this owner correction first and then use its explicit empty-capture semantics from PR1811. No native authority, new public API or default-parameter shim is needed.

PR1812 https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1812 at de6ccbad5c11d14ffdb96c5461f4446d76afce54: one production file plus focused tests/fragment. Old production4failed4passed0skip; exact clean head64passed0failed0skip after two contradiction controls added. Cold plan and implementation review found no BLOCKING findings; docs/skills/format passed,0new format findings. Hosted gates pending; no merge/closure/full-gate claim. Root owns final serialized just ci after trailing docs.
