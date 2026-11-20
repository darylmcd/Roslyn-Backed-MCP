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

## Notes

Operator decision 2026-11-20: refuse on drift everywhere. apply_project_mutation and the solution-store apply routes refuse a project file edited after preview, reusing CompositeSourceFingerprint; no write on refusal. One ADR shared with undo-service-revert-unconditional-restore.

Surface audit 2026-11-20 (server 5.1.1) (finding P13-F1): reproduced with no external editor. Two previews taken against tests/GitFixtureWorkerProbe/GitFixtureWorkerProbe.csproj (add_project_reference_preview, then set_conditional_property_preview), applied in that order with apply_project_mutation: both returned success and the second wrote its own snapshot, silently dropping the ProjectReference the first had added. The refuse-on-drift decision therefore also has to cover the server's own earlier apply, not only outside edits.
