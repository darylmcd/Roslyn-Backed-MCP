# composite-apply-encoding-hygiene-consolidated — consolidated low-severity cleanup in the new encoding helpers

**row:** `composite-apply-encoding-hygiene-consolidated` · **pri:** `Low` · **size:** `S` · **deps:** `atomic-file-cleanup-error-detail-redaction`

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
Two more copies of the encoding-fixture test block landed in PR #1181 (tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs:196, UndoServiceTests.cs:464) — the `encodingKind switch` fixture pattern now appears in 5 test files total, up from 3. [source: PR #1181 code-quality review]
## Amendment — 2026-08-13 (backlog-sweep 20260813T172325Z, PR #1243)

This row's anchors are now partly stale, and the extraction it anticipated has landed.

PR #1243 extracted the encoding sniffing into `src/RoslynMcp.Roslyn/Helpers/SourceFileEncoding.cs` and **deleted** `AtomicFileWriter.ResolveWriteEncoding`. Consequences for this row:

- **Anchor drift:** `CompositeApplyOrchestrator.cs:219` no longer exists. Re-derive this row's anchors before working it.
- **Naming decision to re-make:** this row proposed `ResolveWriteEncodingFromBytes` / `ResolveWriteEncodingFromSourceText`. The shipped names are `SourceFileEncoding.FromBytes` / `FromSourceText`. The quality review noted the shipped names lose the write-intent that `ResolveWriteEncoding` carried, and now sit confusingly beside `Sniff(byte[])` which takes the same argument type but means something different. Decide deliberately rather than inheriting.
- **Three dangling doc references survive** the deletion, all codespans so no build break: `FileSnapshotCapture.cs` (class doc, still cites the deleted member as the reason two entry points exist), `tests/RoslynMcp.Tests/UndoServiceTests.cs`, and `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`. Verified: a repo grep for `ResolveWriteEncoding` returns exactly these three hits post-merge. `FileSnapshotCapture.cs` was deliberately left untouched by #1243 to hold its 7/7 gate-forced file budget.

**Add to this row's acceptance:** all three surviving `AtomicFileWriter.ResolveWriteEncoding` doc references point at the `SourceFileEncoding` members that replaced them; a repo grep for `ResolveWriteEncoding` over `src` and `tests` returns nothing.
