# msbuild-metadata-evaluation-collection-disposal — Dispose owned metadata evaluation collections

**row:** `msbuild-metadata-evaluation-collection-disposal` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs:43` — new ProjectCollection; finally57 unloads but does not dispose.
- `tests/RoslynMcp.Tests/ProjectMetadataParserTryGetResolvedPackageVersionTests.cs`

## Acceptance

- [ ] Verify native collection disposal semantics and current callers, then own and dispose the throwaway collection on success and evaluation failure while preserving unload behavior.
- [ ] Preserve resolver-before-JIT ordering, required globals and public exception classification; reuse actual success/failure controls and observe the ownership defect before repair.
- [ ] Complete independent review and appropriate qualification without inventing a native-policy prerequisite or unrelated refactor.

## Evidence

- Main58ba4207 source creates ProjectCollection with operation globals, calls UnloadAllProjects in finally and never calls Dispose.
- This establishes an ownership omission, not a measured OS-handle or memory leak; no leak reproduction is claimed.
- Existing metadata-global and parser catch-classification rows own different mechanisms.

## Context

- Independent nonblocking follow-up; no restore closure dependency.
