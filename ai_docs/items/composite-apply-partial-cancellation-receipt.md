# composite-apply-partial-cancellation-receipt — Account for outputs published before cancellation

**row:** `composite-apply-partial-cancellation-receipt` · **pri:** `Medium` · **size:** `M` · **verified:** `52f2c418cf5ba982b2a1c0914dd3977655d93eb4` · **deps:** `class-split-generated-file-no-clobber`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Decide how the caller can discover the actual outputs published before a canceled composite apply, even if the requesting client is no longer listening. Reproduce cancellation after the first publication and prove the supported recovery never deletes foreign outputs or displaces an unrelated undo target.

## Evidence

- The composite orchestrator propagates OperationCanceledException; its I/O failure projection reports appliedFiles, but cancellation can follow a successful publication without delivering that result. A client may then lack durable output/recovery evidence.

## Context

- Separate from atomic no-replace creation. Re-vet any cancellation contract change against stable revert_last_apply and MCP cancellation semantics before selection.
