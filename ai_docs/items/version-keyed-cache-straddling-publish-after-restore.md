# version-keyed-cache-straddling-publish-after-restore — Reject cache publishes captured before a version-lowering restore

**row:** `version-keyed-cache-straddling-publish-after-restore` · **pri:** `Low` · **size:** `S` · **deps:** `undo-restore-version-lowering-version-keyed-caches`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:93` — `PublishIfWorkspaceLive` accepts a publish captured at version v+1 after the restore evicted.

## Acceptance

- [ ] Deterministic test: a discovery that captured version v+1 publishes after `RestoreVersion(v)`; the entry is not served on a later read at v+1 with different content.
- [ ] Fix via an epoch/generation captured with the version, applied to every version-keyed cache that publishes asynchronously (list with file:line: CompilationCache, DiagnosticQueryService, NuGetDependencyService, DiRegistrationService, MsBuildEvaluationService).
- [ ] No public contract change.

## Evidence

- Cold plan review (2026-11-12): the `RestoreVersion` event raise evicts but does not cover a read in flight across the restore. Production reach is weak (the write gate excludes readers during an undo); service-level race only.

## Context

- Remainder of `undo-restore-version-lowering-version-keyed-caches`; chained behind it.
