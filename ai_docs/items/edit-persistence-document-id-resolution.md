# edit-persistence-document-id-resolution — carry resolved document identity into persistence

**row:** `edit-persistence-document-id-resolution` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs`

## Acceptance

- [ ] Carry the already-resolved `DocumentId` (or the resolved document identity) into `PersistDocumentTextToDiskAsync`; do not rescan every project/document by path after `TryApplyChanges`.
- [ ] Reacquire the updated document by identity and retain its `SourceText.Encoding` plus the caller-pinned canonical write target.
- [ ] Remove the hard-coded `OrdinalIgnoreCase` path match from this persistence seam in favor of identity.
- [ ] One regression uses two path spellings that the current comparison can conflate or miss and proves the originally resolved document alone is persisted.

## Evidence

`ApplyTextEditsCoreAsync` already owns the resolved `Document`, but persistence discards that identity, walks every document in the updated solution, and selects the first `FilePath` matching a hard-coded case-insensitive comparison. This is unnecessary work and can select the wrong linked/case-distinct document on case-sensitive filesystems.
