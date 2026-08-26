# codeaction-preview-truncation-flag-hardcoded-false — derive the code-action truncation flag from the diff sentinel

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs` (the `Store(..., diffTruncated: false, kind: PreviewKind.CodeAction)` call)
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs` (the changes-shaped `Store` overload deriving the flag via `ContainsTruncatedSentinel`)
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (the `if (diffTruncated && !force)` gate)
- `tests/RoslynMcp.Tests/CodeActionServiceTests.cs`

## Acceptance

- [ ] `CodeActionService.PreviewCodeActionAsync` stores its token through the changes-shaped `Store(..., changes, PreviewKind.CodeAction)` overload so `DiffTruncated` reflects the `<truncated>` sentinel, matching the code-fix producer fixed in PR #1344.
- [ ] A test asserts a code-action preview whose diff carries the truncation sentinel records `DiffTruncated=true` and is refused by the apply path without `force: true`.

## Evidence

Traced at the call site by the cold code-quality review of PR #1376 (sweep `20260825T214500Z`), not hypothesized. `SolutionDiffHelper.ComputeChangesAsync` appends a truncation sentinel past its total-chars cap; `CodeActionService` receives that `changes` list but passes `diffTruncated: false` to `Store` anyway. `RefactoringService`'s `if (diffTruncated && !force)` is the only gate, so it is dead for every code-action token and a truncated code-action preview applies to disk blind.

Not a regression — the prior 4-argument overload forwarded `false` too — but PR #1376 made the value explicit and annotated it as deliberate, which reads as vetted. PR #1344 fixed the identical shape for the code-fix producer.

## Context

Sibling row `preview-store-codefix-truncation-contract-coverage` covers the code-FIX producer and is test-only, so this needs its own production-fix row rather than an amendment.
