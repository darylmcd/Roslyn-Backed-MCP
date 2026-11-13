# undo-plan-token-version-equality-survives-undo — Invalidate version-equality plan tokens and post-apply preview entries when undo lowers the workspace version

**row:** `undo-plan-token-version-equality-survives-undo` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BuildService.cs:55` — plan token stores the workspace version; `:84` compares by equality.
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:337` — same version-equality plan token.
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:276` — same pattern.
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:282` — `InvalidateOnVersionBump` prunes on reload only; entries made after the apply survive a no-reload undo.

## Acceptance

- [ ] A plan token minted at version V+1 after an apply is rejected after undo lowers the version and a later apply returns to V+1 with different content.
- [ ] Same for preview-store entries created after the apply when the undo path does not reload.
- [ ] No tool name, parameter, response shape or documented preview/plan-token semantic changes (docs/release-policy.md:21); if a per-session generation key would alter documented semantics, stop and take an operator decision first.
- [ ] Red-first deterministic test per mechanism.

## Evidence

- Found by the cold plan review of the `undo-restore-version-lowering-version-keyed-caches` fix (2026-11-12): that fix raises `WorkspaceReloaded` from `RestoreVersion`, which evicts caches but does not cover plan tokens that compare version by equality.

## Context

- Remainder of `undo-restore-version-lowering-version-keyed-caches`; chained behind it. Operator flag: a generation key may touch documented preview/plan-token semantics.
