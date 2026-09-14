# type-move-directive-context-preservation

**row:** `type-move-directive-context-preservation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Preserve or safely refuse moves whose effective nullable or conditional directive context cannot be reconstructed in both files. Pin a declaration under a preceding nullable directive attached to a sibling.

## Evidence

CreateMovedCompilationUnit copies imports and namespace ancestors but does not reconstruct effective directives preceding the selected declaration; RemoveNode can also leave split directive pairs. Verified during 2026-09-14 direct type-move remediation.
