# apply-project-mutation-last-apply-wins-decision — Decide whether apply_project_mutation refuses a csproj edited after preview

**row:** `apply-project-mutation-last-apply-wins-decision` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:536-575`
- `src/RoslynMcp.Roslyn/Services/CompositeSourceFingerprint.cs`

## Acceptance

- [ ] Operator decision recorded in an ADR: keep documented last-apply-wins, or refuse a project file changed between preview and apply.
- [ ] If refuse: reuse `CompositeSourceFingerprint`, red-first test for a csproj edited between preview and apply, no write on refusal.
- [ ] Decide whether solution-store `*_apply` routes (which rely on `PeekChangedPaths` plus a version span) need the same on-disk check.

## Evidence

- PR #2015 plan/review: composite previews now refuse on drift; `ProjectMutationService` deliberately documents last-apply-wins with a memory-only store. Needs an operator decision before code.
