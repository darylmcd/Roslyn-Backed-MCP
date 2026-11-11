# split-service-compose-same-file-registration-mutation — Compose service split edits to the same source file

**row:** `split-service-compose-same-file-registration-mutation` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:535-574`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] A valid registration in the same file as the split source yields one combined mutation and diff. The applied and reloaded graph preserves both intended edits and compiles.

## Evidence

- Source review at `34a87d9c`. The facade is staged first, then the registration rewrite rereads original disk text and stages a second full replacement for the same path.
