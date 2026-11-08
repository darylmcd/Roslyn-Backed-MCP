# composite-preview-built-from-stale-workspace-text — Composite producers hash the workspace snapshot, not the disk bytes they read

**row:** `composite-preview-built-from-stale-workspace-text` · **pri:** `Medium` · **size:** `M` · **deps:** `composite-preview-source-fingerprint-refusal`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs` (`Store` captures fingerprints from disk at Store time)
- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:97`
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:110`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Red first: a target edited on disk after the workspace snapshot but before the watcher reloads, then previewed, is detected at apply (or the preview refuses) instead of being overwritten with `UpdatedContent` built from the stale snapshot.
- [ ] Fingerprint the same `SourceText` bytes the producer read, or compare document text to disk in the producers; ADR 0033 residual window is closed or narrowed with evidence.

## Evidence

- PR #2015 review: producers compute `UpdatedContent` from the workspace snapshot; the Store-time disk fingerprint cannot reveal an edit that landed before Store. Named in `docs/decisions/0033-composite-preview-source-fingerprints.md`.
