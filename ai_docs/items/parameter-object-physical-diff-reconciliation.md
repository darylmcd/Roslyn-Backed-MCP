# parameter-object-physical-diff-reconciliation — Reconcile parameter-object preview by physical file

**row:** `parameter-object-physical-diff-reconciliation` · **pri:** `Medium` · **size:** `S` · **deps:** `cross-project-interface-signature-compile-gate`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:131`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1808`
- `tests/RoslynMcp.Tests/CaseDistinctCallsitePreviewTests.cs:56`

## Acceptance

- [ ] Before storing a parameter-object preview, inspect every document sharing each changed physical path, including linked owners absent from `originalTexts`. Propagate an identical safe edit to all owners or refuse; emit one physical diff only when their before/after content agrees.
- [ ] Refuse an added DTO path that collides with a document in any project or an existing physical file. Verify the pending and applied solutions compile where the route requires it.
- [ ] Keep distinct case-variant paths independent on a case-sensitive filesystem. On Windows, refuse a case-variant pair with divergent content before returning a token.
- [ ] Red-first linked-document and case-variant tests cover the preview and apply boundary; verify callsite summaries remain consistent with the physical changes.

## Evidence

- On 2026-11-08, `BuildFileChangesAsync` creates one `FileChangeDto` for each changed `DocumentId` and a separate DTO addition. `PreviewParameterObjectAsync` stores the accumulator and these changes without checking whether several document paths identify one physical file. The Windows case-distinct test asserts only an aggregated callsite count while its `Caller.cs` and `caller.cs` documents contain divergent source text. The corresponding bulk-refactoring preview refused the same impossible physical write in PR #2030's hosted Windows shard; the parameter-object route remains unguarded.

## Context

- Reuse the physical-path reconciliation contract established by `cross-project-interface-signature-compile-gate` where applicable. Keep the parameter-object route's DTO addition and callsite projection behavior explicit.
