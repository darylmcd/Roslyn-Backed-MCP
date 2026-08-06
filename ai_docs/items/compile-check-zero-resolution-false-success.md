# compile-check-zero-resolution-false-success — compile_check reports Success=true when a files filter matches zero workspace documents

**row:** `compile-check-zero-resolution-false-success` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs` (the `files`-filter zero-resolution fallback; `Success: acc.ErrorCount == 0 && !acc.Cancelled && acc.CompletedProjects > 0`)

## Acceptance

- [ ] When a `files` filter resolves to zero workspace documents, `compile_check` no longer reports `Success: true` — either it fails loudly, or the response structurally discloses the zero-match fallback (the existing `RequestedScope`/`ActualScope` pair is the natural carrier) instead of leaving it to `restoreHint` prose.
- [ ] Regression test: a `files` filter naming paths outside the loaded workspace against a solution with real compile errors must NOT return `Success: true, ErrorCount: 0`.

## Evidence

- Surfaced by the `validate-workspace-diagnostic-harvest-reconcile` fix-cycle executor (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1160) while validating in an unrestored worktree: an unscoped `compile_check` returned 18,274 package-resolution errors, but a `files`-scoped check against the same worktree hit the zero-resolution fallback (`"supplied file scope did not resolve to any loaded workspace document"`), widened to full-solution scope, filtered returned diagnostics by path down to zero, and reported `success:true, errorCount:0, completedProjects:5` against a solution carrying 18k real errors.

## Context

Distinct from the existing `validate-workspace-overallstatus-false-positive`-class rows, which cover the `CompletedProjects==0` arm (nothing ran) — this is the "completed everything, matched nothing" arm: the compile genuinely ran to completion, just never touched the files the caller asked about, and the tool reports success anyway.
