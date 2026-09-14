# type-move-file-local-binding-safety

**row:** `type-move-file-local-binding-safety` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Reject moving file-local declarations or types that depend on file-local symbols unless identity and binding can be preserved. Pin a moved ordinary type whose base or member references a file-local sibling.

## Evidence

TypeMoveService selects and copies declarations to a different syntax tree without checking IsFileLocal on the selected symbol or its dependencies; file-local siblings remain visible only in the original tree. Verified during 2026-09-14 direct type-move remediation.
