# workspace-not-found-next-major-category-promotion — at the 5.0 cut, promote reason WorkspaceNotFound to the error category

**row:** `workspace-not-found-next-major-category-promotion` · **pri:** `Defer` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs`
- `docs/product-contract.md`
- `tests/RoslynMcp.Tests/WorkspaceNotFoundWireContractTests.cs`
- `tests/RoslynMcp.Tests/ToolErrorHandlerSpecificityTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceEvictionAutoRetryTests.cs`

## Acceptance

- [ ] Start only at the 5.0.0 cut (operator decision 2026-09-28: additive in 4.x, contract change at 5.0).
- [ ] An unknown `workspaceId` returns `category: "WorkspaceNotFound"` and `exceptionType: "WorkspaceNotFoundException"`; ADR 0011 decides whether `reason` stays for one major as an alias or is dropped.
- [ ] The in-call auto-reload race path (`ToolErrorHandler.TryClassifyReloadRace`) stops keeping the 4.x `WorkspaceReloadedDuringCall` category and `KeyNotFoundException` wire name for a `WorkspaceNotFoundException` and returns the same `WorkspaceNotFound` envelope; `ToolErrorHandlerSpecificityTests.UnknownWorkspaceId_AfterAutoReload_KeepsV4ReloadRaceWireValues_AndAddsWorkspaceReason` is renamed and re-pinned.
- [ ] `ResourceReadResultFilter` maps the new category to the not-found code and emits the `WorkspaceNotFound:` prefix; the every-category mapping test stays green.
- [ ] `WorkspaceNotFoundWireContractTests` and the classifier tests re-pin the 5.0 values; symbol, file, and metadata-name misses stay `NotFound`.
- [ ] ADR 0011 (`v5-major-release-contract-prereqs`) records old→new wire behavior, and the `Changed — BREAKING` fragment carries the consumer migration note.

## Evidence

- #1571 (`09728c88`) changed the category inside 4.x; branch `fix/additive-4x-breaking-rework` restored `NotFound` + `KeyNotFoundException` and added the optional `reason: "WorkspaceNotFound"` discriminator so main can ship 4.3.0.

## Context

The internal `WorkspaceNotFoundException` stays. 4.x pins the wire values through the `ErrorInfo.Reason` and `ErrorInfo.WireExceptionType` fields set by its `ToolErrorHandler` entry; remove the `WireExceptionType` override at 5.0.
