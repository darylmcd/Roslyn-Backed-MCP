# workspace-load-missing-assets-auto-restore — Restore missing assets on omitted opt-in

**row:** `workspace-load-missing-assets-auto-restore` · **pri:** `High` · **size:** `M` · **deps:** `workspace-restore-safe-execution,workspace-restore-budget,workspace-restore-packages-path,workspace-restore-public-failure`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:46-121`
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:218-268`
- `src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs:136-165`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadAutoRestoreTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs`
- `docs/product-contract.md`
- `docs/decisions/README.md`

## Acceptance

- [ ] `workspace_load` and `workspace_reload` accept nullable `autoRestore`: omitted restores only missing assets; true preserves explicit restore for drift; false opts out.
- [ ] Detect the missing assets file through the project's own intermediate-output layout, including `UseArtifactsOutput`.
- [ ] A default restore failure or timeout leaves a successful load with `restoreRequired:true` and a path-free reason; caller cancellation propagates; explicit true still fails the call.
- [ ] Red-first tests cover omitted/true/false, missing assets, drift, timeout, and failure.
- [ ] Record the additive 4.x compatibility decision in a new ADR and document behavior in the product contract and `Changed` fragment.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:46,111` defaults `autoRestore=false`, so omitted and explicit false are indistinguishable; `RestoreStalenessDetector.cs:218-268` conflates missing assets and drift.

## Context

- Depends on the safe restore execution slice; otherwise making restore the default would amplify build/restore races and unbounded load latency.
