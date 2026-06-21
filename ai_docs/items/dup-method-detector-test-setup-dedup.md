# dup-method-detector-test-setup-dedup — de-duplicate the test workspace builder

## Anchors

- `tests/RoslynMcp.Tests/DuplicateMethodDetectorTests.cs` (`BuildServiceAndGate` ~`:894` copy-pastes the AdhocWorkspace/ProjectInfo/DocumentInfo construction in `BuildServiceWithSourcesCore` ~`:939`)

## Acceptance

- [ ] `BuildServiceAndGate` reuses the existing single-source workspace builder (expose the workspace/manager from `BuildServiceWithSourcesCore`) instead of duplicating ~22 lines; the only delta should be the trailing `WorkspaceExecutionGate` construction.
- [ ] All summary-mode and existing detector tests still pass.

## Evidence

PR #1011 added `BuildServiceAndGate`, which copies the existing `BuildServiceWithSourcesCore` workspace setup verbatim except for the added gate. Source: 2026-06-21 backlog-sweep code-quality review of PR #1011 (severity: medium → test-only dedup, filed Low).

## Context

Test-helper cleanup; no production change.
