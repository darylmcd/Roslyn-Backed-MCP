# parameter-object-retired-design-doc-links — Replace retired parameter-object contract links

**row:** `parameter-object-retired-design-doc-links` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/ParameterObjectPreviewRequest.cs`
- `src/RoslynMcp.Core/Services/IParameterObjectService.cs`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Replace every reference to deleted `ai_docs/items/parameter-object-preview-design.md` with a durable public contract document or self-contained wording.
- [ ] Keep refusal, generated-DTO, and preview-redemption guidance consistent across the model, service contract, implementation, tool surface, and regression header.
- [ ] Add one documentation-link inventory assertion or equivalent deterministic check that the retired path no longer appears.

## Evidence

- The referenced design item is absent, so five live XML/test comments route maintainers to a dead contract.
