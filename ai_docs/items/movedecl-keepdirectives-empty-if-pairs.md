# movedecl-keepdirectives-empty-if-pairs — KeepDirectives leaves empty #if/#endif pairs in retained files

**row:** `movedecl-keepdirectives-empty-if-pairs` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/MovedDeclarationFileWriter.cs`
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs`

## Acceptance

- [ ] Retained files contain no empty conditional pairs; disabled text above the moved type is placed deliberately and pinned by a test.

## Evidence

- MovedDeclarationFileWriter.cs ~93; PR 1992: disabled text above Selected moves into the new namespace.
