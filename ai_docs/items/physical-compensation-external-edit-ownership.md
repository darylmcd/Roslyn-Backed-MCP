# physical-compensation-external-edit-ownership — Preserve external edits during failed persistence compensation

**row:** `physical-compensation-external-edit-ownership` · **pri:** `High` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:400`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:428`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Inventory current failed-persistence compensation writes and deletions; track the exact writes actually completed by the operation, rather than treating every captured path as modified.
- [ ] Use a deterministic failed later write plus externally changed earlier path to prove external bytes are preserved. Cover existing-file replacement, initially absent-file creation, untouched captured paths and unchanged owned post-state rollback controls.
- [ ] Only compensate a destination still owned by this operation; surface ownership conflict and preserve primary plus independent cleanup failures. Revalidate destructive actions across applicable asynchronous boundaries and concurrent edits; byte comparison alone must not be claimed atomic protection.
- [ ] Implement the smallest complete current persistence ownership correction or record a concrete product decision blocker. Do not require or silently approve the retained physical directory lease/recovery callback architecture; its separate tests and acceptance stay held.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. INVESTIGATION/current destructive boundary: RollBackAsync400-428 iterates captured paths and unconditionally restores original bytes or deletes an existing destination without testing operation post-state ownership. Current source supports an external-edit risk; this audit did not execute a runtime reproduction. Retained guard/lease/callback extensions remain prospective.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
