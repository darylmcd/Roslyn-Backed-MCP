# composite-apply-encoding-hygiene-consolidated — consolidated low-severity cleanup in the new encoding helpers

**row:** `composite-apply-encoding-hygiene-consolidated` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs:95` (duplicated encoding-fixture arrange/assert block)
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:133` (stale `AtomicFileWriter` class doc claims the encoding problem is fully solved)
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:219` (`ResolveWriteEncoding` overload naming ambiguity)

## Acceptance

- [ ] The ~25-line encoding-fixture arrange/assert block copy-pasted into `ApplyTextEditVerifyTests.cs:382`, `EditorConfigServiceTests.cs:95`, and `ProjectMutationIntegrationTests.cs:601` is hoisted into `IsolatedWorkspaceTestBase` so a future encoding case is added once.
- [ ] The `AtomicFileWriter` class doc paragraph at `:133` names `ApplyCompositeAsync` as a known remaining lossy caller (or is corrected once `composite-apply-undo-encoding-still-lossy` ships).
- [ ] The two `ResolveWriteEncoding` overloads are renamed (`ResolveWriteEncodingFromBytes` / `ResolveWriteEncodingFromSourceText`) so intent is readable and an untyped `null` at a future call site isn't ambiguous.

## Evidence

- Code-quality review of PR #1157 (`mutation-write-paths-drop-original-encoding`): three low-severity findings — duplicated test fixture across 3 files, a class-doc paragraph overstating what's fixed, and an ambiguous overload pair.

## Context

Spin-off from the `mutation-write-paths-drop-original-encoding` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1157). All hygiene, not functional bugs; consolidated per the sweep's filing gate.
