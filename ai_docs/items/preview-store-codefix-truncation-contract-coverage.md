# preview-store-codefix-truncation-contract-coverage — cover the code-fix preview truncation-flag change

**row:** `preview-store-codefix-truncation-contract-coverage` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:641`
- `tests/RoslynMcp.Tests/PreviewStoreTests.cs`

## Acceptance

- [ ] A test asserts a code-fix preview whose diff carries the truncation sentinel stores `DiffTruncated=true`.
- [ ] A test asserts that preview is refused by the apply path unless `force: true`.
- [ ] The behavior change is recorded in a changelog/doc note — the shipping fragment described the PR as tagging-only.

## Evidence

Traced at the call site in PR #1344: the pre-diff line called the 4-arg `Store(workspaceId, newSol, version, desc)` overload (`diffTruncated` defaults false); the post-diff line calls the changes-shaped overload, which sets `diffTruncated: ContainsTruncatedSentinel(changes)` (`src/RoslynMcp.Roslyn/Services/PreviewStore.cs:145-168`). `IPreviewStore`'s own doc states the apply path refuses truncated previews without `force`, so the redemption outcome for a truncated code-fix preview changed. No test in the diff exercises it.

## Context

Surfaced by the cold code-quality reviewer on `preview-token-store-provenance-contract` (PR #1344, sweep `20260825T151721Z`). Advisory medium — did not block the land — but it is a silent behavior change on a user-visible refusal path.
