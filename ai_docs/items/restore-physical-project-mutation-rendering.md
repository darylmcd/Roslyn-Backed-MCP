# restore-physical-project-mutation-rendering — Render project mutations from original physical XML

**row:** `restore-physical-project-mutation-rendering` · **pri:** `Medium` · **size:** `M` · **deps:** `workspace-restore-packages-path`

## Anchors

- (new) `src/RoslynMcp.Roslyn/Services/ProjectReferenceMutationRenderer.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs`
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Bind reference and explicit Compile operations to original XML nodes before effects; aggregate compatible physical contexts once and reject contradictory retained or absent sibling requirements.
- [ ] Preserve encoding, conditions, unknown metadata, comments and build-only references. Refuse concrete ambiguous conditional metadata/item bindings before effects; preserve unrelated representable edits.
- [ ] Use the same prepared rendering for preview project diffs, distinct token write paths and source persistence; retain rollback and existing undo behavior.
- [ ] Observe alias-swap, overlapping-context, conditional-item/metadata and source pre-effect regressions fail before repair; preserve ordinary single-context additions and explicit Compile changes.
- [ ] Land this four-production-file slice independently. Exclude apply-path reference rebasing and the new UTF-16 sibling-rebase undo regression; those have separate dependent rows.
- [ ] Obtain fresh independent review without blocking findings, scoped consumer checks and required hosted checks; close only after verified merge and the full serialized delivery gate.

## Evidence

| Reference | Use | Limit |
|---|---|---|
| Draft PR1808, commit eea2f083148251f2c66dd09c2693575043388f1b | Renderer/source/preview implementation and regressions | Five production files plus UTF-16 test are bundled; do not merge the draft whole. |
| Draft PR1809, commit b88d8c180dd057224da32aa24fe44b91dafb5420 | Earlier scoped checkpoint | 23 files versus 10-file plan triggered the scope stop. |
| PR1806 branch origin/codex/restore-reviewed-graph; stash codex-remediate-20261022-preserve-restore | Read-only historical reference | Never pop/drop the stash or import the large tree. |
| ~/.codex/scratch/roslyn-remediate-20261023-split/ | Current-session RED logs and 84-pass combined scoped cohort | Combined cohort is not independent-slice or full-producer proof. |

## Context

- Reference drafts: https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1808 and https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1809.
- Scope stop: 23 changed files, +1991/-247, approximately 2.3 times the planned ten files. No further fixes belong on either held draft.
- Dependency chain: renderer → apply-path rebase → UTF-16 undo → restore row. Re-derive each independently shippable boundary before implementation.
