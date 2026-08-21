# prompt-service-parameter-classifier-consolidation — Single-source prompt service-parameter classification

**row:** `prompt-service-parameter-classifier-consolidation` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/PromptParameterIndex.cs`
- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs`
- `tests/RoslynMcp.Tests/PromptShimToolsTests.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`

## Acceptance

- [ ] Move service-parameter recognition to one shared classifier consumed by prompt cataloging and shim dispatch.
- [ ] Schema exposure and runtime argument construction agree for every supported injected service type.
- [ ] One table-driven regression adds a sentinel service type and proves both consumers classify it identically.

## Evidence

- `PromptParameterIndex` and `PromptShimTools` maintain mirrored `IsServiceType` logic, so a new injected parameter can be advertised and dispatched inconsistently.
