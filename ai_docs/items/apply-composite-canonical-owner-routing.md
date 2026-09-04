# apply-composite-canonical-owner-routing — Canonicalize composite-apply ownership and workflow hints

**row:** `apply-composite-canonical-owner-routing` · **pri:** `Low` · **size:** `M` · **deps:** `apply-composite-canonical-alias-surface`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs`
- `tests/RoslynMcp.Tests/SymbolRefactorPreviewTests.cs`

## Acceptance

- [ ] Make internal workflow hints and prompts recommend `apply_composite`.
- [ ] Record the canonical operation name in the change ledger regardless of which alias invoked it.
- [ ] Prove the change ledger and workflow hints expose only the canonical owner name.

## Evidence

The four named surfaces still embed the deprecated external name as the internal owner or preferred workflow route.

## Context

Depends on `apply-composite-canonical-alias-surface`. Migration child split from `tool-consolidation-adr-and-alias-machinery`.
