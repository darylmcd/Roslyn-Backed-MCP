# preview-project-reference-metadata-rebase — Preserve metadata replacement across sibling applies

**row:** `preview-project-reference-metadata-rebase` · **pri:** `Medium` · **size:** `S` · **deps:** `restore-physical-project-mutation-rendering`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:996`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Rebase removed reference intents before additions; match the original target and aliases/embed metadata so a replacement is neither suppressed nor removed afterward.
- [ ] Preserve unrelated sibling document/reference edits and intended preview metadata across workspace lineage changes; refuse conflicting same-edge requirements before persistence.
- [ ] Demonstrate the old add-before-remove defect with a real public preview, successful unrelated sibling apply and later original-token redemption; assert the intended edge remains and sibling edits survive.
- [ ] Keep encoding/undo qualification in the dependent UTF-16 row; ship this one-production-file correction with semantic rebase regressions and no fork/native-authority expansion.
- [ ] Obtain fresh independent review and required local/hosted checks; close only after verified merge and the full serialized delivery gate.

## Evidence

- Main RebaseProjectReferences adds only when a target ID is absent, then removes the old same-target reference; metadata replacement after a sibling apply loses the edge.
- Draft PR1808 commit eea2f083148251f2c66dd09c2693575043388f1b preserves the bounded correction; PR1809 commit b88d8c180dd057224da32aa24fe44b91dafb5420 preserves an earlier version. Do not merge either bundle.
- Current-session public-sibling-metadata-rebase-red.log observed the public apply mismatch before repair; a no-sibling test passed and did not prove rebase coverage.
- Read-only references: origin/codex/restore-reviewed-graph and stash codex-remediate-20261022-preserve-restore; never pop/drop the stash.

## Context

- Drafts: https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1808 and https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1809.
- Operator scope hold followed 23 files versus a ten-file plan. Start a separately reviewable slice; do not resume adding fixes to the drafts.
