# cross-project-refactoring-refusals-public-message — Return cross-project refactoring refusals verbatim

**row:** `cross-project-refactoring-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:764`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] move_type_to_project_preview whose target would create a reference cycle returns the refusal naming both projects (CrossProjectRefactoringService.cs:764).
- [ ] Same-project (:46) and target-file-exists (:79, :174, :805) refusals state their reason; file-exists messages name the file relative to the project, never an absolute path.
- [ ] Regression test asserts the cycle refusal's public message through the tool error envelope.

## Evidence

- move_type_to_project_preview cycle refusal returned "The operation is not valid in the current state. Check the tool contract and retry." — mcp-surface-audit 20260924-1305 (check C1). Throw sites are plain `InvalidOperationException`, which `ToolErrorHandler` genericizes.

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Convert only server-authored refusals to `PublicInvalidOperationException`; leave internal-invariant throws ("Semantic model could not be created…") generic.

## Notes

- 2026-09-28: mechanism parent `tool-refusal-public-message-guard` adds a ratchet that fails any new plain `InvalidOperationException` under `src/`; this row is part of its burn-down. Convert this row's sites to `PublicInvalidOperationException` (or the Core internal factory for true invariants) and lower the committed baseline count in the same PR.
