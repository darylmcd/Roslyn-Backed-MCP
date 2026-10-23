# fork-document-text-encoding — Preserve document encoding during fork replay

**row:** `fork-document-text-encoding` · **pri:** `Medium` · **size:** `S` · **deps:** `restore-active-assets-layout-authority`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:650` — Encoding-less file write discards SourceText.Encoding.
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceForkDocumentEncodingTests.cs`

## Acceptance

- [ ] The shared document replay writer preserves SourceText encoding and BOM for changed and added documents, with exact fork bytes matching the reviewed text and source bytes/version/token authority preserved.
- [ ] Observe the old writer lose the UTF-16 BOM through the public fork path; include changed/added routes and default encoding controls without altering graph behavior.
- [ ] Obtain independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- One writer/encoding mechanism; one production file, one test file and one fragment.
- Dependency on active assets authority records ordered delivery, not a technical prerequisite.

## Evidence

- restore-fork-red.log: expected UTF-16 BOM byte255, actual first byte110 after public fork replay; 2 genuine fork failures before any fork production edits.
- Preserve the existing renderer's separate project-file byte handling; do not import graph machinery here.
