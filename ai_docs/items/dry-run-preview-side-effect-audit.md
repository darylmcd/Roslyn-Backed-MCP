# Dry-run preview side-effect audit
<!-- purpose: Evidence note for dry-run-preview-side-effect-audit. -->

**Initiative:** `dry-run-preview-side-effect-audit`
**Date:** 2026-05-08
**Scope:** audit-only; no production behavior change.
**Outcome:** no workspace or disk side effects confirmed beyond expected preview-token storage.

## Question

Backlog evidence suggested some `_preview` calls might mutate workspace caches, reload state, or disk files during plan-only use. The production apply paths clearly mutate workspace and disk state, but preview wrappers are intended to build immutable preview state and return a token for a later apply.

## Evidence Captured

Added `DryRunPreviewSideEffectAuditTests` with representative preview-only calls:

| Preview path | State captured before/after | Result |
|---|---|---|
| `RefactoringService.PreviewRenameAsync` | workspace version, current solution changes, source bytes, project bytes, preview-token retrieval | No workspace version bump, no current-solution changes, no disk-byte changes. Token storage confirmed. |
| `TypeMoveService.PreviewMoveTypeToFileAsync` | workspace version, current solution changes, source bytes, project bytes, target file existence, preview-token retrieval | No workspace version bump, no current-solution changes, no source/project byte changes, and no target file created. Token storage confirmed. |
| `ProjectMutationService.PreviewAddPackageReferenceAsync` | workspace version, current solution changes, source bytes, project bytes, target file existence, preview result changes | No workspace version bump, no current-solution changes, and no project-file byte changes. Preview reports the project-file diff without writing it. |

## Decision

No real preview-side workspace or disk mutation was confirmed. The only confirmed state change is expected preview-token/cache storage so a later apply call can redeem the preview. This closes the investigation row as evidence-only.

## Follow-up Recommendation

Do not split a `dryRun` implementation row from this audit. If a future external session reproduces a concrete preview-only mutation, create a targeted row for the affected tool family with the exact preview call, touched path, and before/after state evidence.
