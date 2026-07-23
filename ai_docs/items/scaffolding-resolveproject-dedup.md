# scaffolding-resolveproject-dedup — Consolidate the duplicated ResolveProject helper via IGatedCommandExecutor

**row:** `scaffolding-resolveproject-dedup` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeScaffolder.cs:59`

## Acceptance

- [ ] `TypeScaffolder` resolves projects via the shared `IGatedCommandExecutor.ResolveProject` seam (or a single shared helper) rather than a private copy
- [ ] No more than one non-executor copy of the `ProjectStatusDto` `ResolveProject` body remains among `ScaffoldingService`/`ProjectMutationService`/`TypeScaffolder`

## Evidence

- Traced during code-quality review of PR #1099 (`scaffolding-type-collaborator-extraction`): `TypeScaffolder.cs:59` duplicates `ScaffoldingService.cs:467` and `ProjectMutationService.cs:629` verbatim (both reference-only here, not anchors — this row's fix is scoped to `TypeScaffolder.cs`), while `IGatedCommandExecutor.cs:40` already exposes an injectable `ResolveProject` that `BuildService`/`TestRunnerService`/`NuGetDependencyService` consume.

## Context

Follow-on from the scaffolding-hotspot decomposition series (`scaffolding-type-collaborator-extraction` → `scaffolding-single-test-collaborator-extraction` → `scaffolding-batch-first-test-collaborator-extraction` → `scaffolding-hotspot-complexity-reduction`). The new `TypeScaffolder` collaborator copied the resolver instead of consuming the existing seam, extending an already-3x-duplicated helper to a 3rd/4th site.
