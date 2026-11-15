# workspace-finalizer-double-physical-resolution — Qualify physical path revalidation during workspace load

**row:** `workspace-finalizer-double-physical-resolution` · **pri:** `Low` · **size:** `S` · **verified:** `d9e2b3ffd8ab69e6bc20b0b06fe4fe7d57d63dd2`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceLoadFinalizer.cs:76-99`
- `tests/RoslynMcp.Tests/WorkspaceLoadLinkRefusalTests.cs`

## Acceptance

- [ ] Reproduce a link transition between in-project containment classification and physical-path refusal. Establish which observation admits the document and enforce consistent path identity without weakening the refusal for a linked in-project target.

## Evidence

- The finalizer resolves each candidate document to check containment, then the shared guard resolves it again to check for links. The two lookups can observe different paths. Static review of the guard extraction; no runtime witness yet.

## Context

- Load-time observation is distinct from a checked ancestor changing before an edit write.
