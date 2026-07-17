# scaffolding-hotspot-complexity-reduction — Reduce remaining scaffolding hotspots

**row:** `scaffolding-hotspot-complexity-reduction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:343-447`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs:399-540`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] `BuildTestContent` accepts one request/context record instead of 11 positional parameters.
- [ ] `BuildArgExpression` and `TrimUsingsToReferencedNamespaces` each measure cyclomatic complexity below 15 after the prerequisite moves; every new or changed helper is also below 15.
- [ ] Tests pin collection, dictionary, interface, abstract, concrete, and NSubstitute argument branches plus MSTest, xUnit, and NUnit output.
- [ ] Roslyn complexity metrics confirm the thresholds and all scaffolding regressions pass.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `BuildArgExpression` at CC 18, `TrimUsingsToReferencedNamespaces` at CC 19, and `BuildTestContent` with 11 parameters.

## Dependencies

- `scaffolding-batch-first-test-collaborator-extraction`
