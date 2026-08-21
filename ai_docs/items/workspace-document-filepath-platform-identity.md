# workspace-document-filepath-platform-identity — centralize document path matching

**row:** `workspace-document-filepath-platform-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolResolver.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadDedupTests.cs`
- `tests/RoslynMcp.Tests/SymbolResolverTests.cs`

## Acceptance

- [ ] Normal and generated document file-path lookup uses the shared canonical filesystem identity contract rather than hard-coded `OrdinalIgnoreCase` comparisons.
- [ ] A platform-aware regression proves case-distinct document paths remain distinct on Linux/macOS while Windows lookup remains case-insensitive.

## Evidence

- Workspace loaded-path dedup and ownership now use `FileSystemPath`, but adjacent document lookup sites still re-spell `StringComparison.OrdinalIgnoreCase`, producing inconsistent identity on case-sensitive filesystems.

## Context

Limit this row to document path resolution. Keep workspace loaded-path identity and ownership in the completed issue #1129 rows.
