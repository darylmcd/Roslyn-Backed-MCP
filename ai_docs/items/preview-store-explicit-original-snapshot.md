# preview-store-explicit-original-snapshot — Require explicit original snapshots when storing previews

**row:** `preview-store-explicit-original-snapshot` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:68` — legacy compatibility overload.
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:94-95` — original inferred from `modifiedSolution.Workspace.CurrentSolution`.
- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:78` — one of about 36 `Store(` call sites under `src` that must pass the original explicitly.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:130` — call site.
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:428` — call site.
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs:236` — call site.

## Acceptance

- [ ] Show red (or disprove with evidence) a producer whose base differs from `Workspace.CurrentSolution` at store time (reload or sibling transition); the failure is apply reverting or dropping changes.
- [ ] Delete the inferring/defaulting overloads; every producer and test passes original, truncation and kind explicitly; MCP wire shapes unchanged.
- [ ] CHANGELOG note for the in-process API change; check `docs/release-policy.md` for whether an ADR is required.

## Evidence

- 2026-10-27: inference is only consistent today because `WorkspaceManager.GetCurrentSolution` (`WorkspaceManager.cs:1037`) returns `session.Workspace.CurrentSolution`. Closed PR1806's ADR (explicit preview snapshot authority) required explicit originals for exactly this reason. Not reproduced.

## Context

- Blocks any manager-side corrected solution; see `project-reference-reload-embed-metadata`.
