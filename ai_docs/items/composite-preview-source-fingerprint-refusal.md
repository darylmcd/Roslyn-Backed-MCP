# composite-preview-source-fingerprint-refusal — Bind composite previews to the source they were computed from

**row:** `composite-preview-source-fingerprint-refusal` · **pri:** `High` · **size:** `M` · **deps:** `composite-token-portable-workspace-identity`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- (new) `src/RoslynMcp.Roslyn/Services/CompositeSourceFingerprint.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] An edit of a target file between preview and apply (same host and cross host) refuses without writes or claim loss.
- [ ] Created-file and deleted-file mutations fingerprint absent/present correctly; format v3 is additive and v2 still reads.

## Evidence

- `CompositeApplyOrchestrator.cs:45` drops the version check and `CompositeFileMutation` carries no pre-image, so no changed-source refusal exists today. Ship with its dependency in the same release.
