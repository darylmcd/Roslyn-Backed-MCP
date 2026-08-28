# fixall-target-resolution-collaborator-extraction — Extract FixAll target resolution

**row:** `fixall-target-resolution-collaborator-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs`
- `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs`

## Acceptance

- [ ] Extract scope parsing, required-input validation, and document/project/solution target selection behind one internal collaborator.
- [ ] Keep malformed-input errors ahead of provider discovery and preserve exact target-selection behavior.
- [ ] One table-driven regression covers document, project, solution, and missing-required-input outcomes.

## Evidence

The 2026-08-28 complexity review measured `PreviewFixAllAsync` at 149 lines and `ResolveTargets` at complexity 10. Scope policy is a separable responsibility and already has a coherent table-driven regression shape.
