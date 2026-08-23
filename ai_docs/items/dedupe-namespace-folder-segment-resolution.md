# dedupe-namespace-folder-segment-resolution — Share namespace folder-segment resolution

**row:** `dedupe-namespace-folder-segment-resolution` · **pri:** `Low` · **size:** `M` · **deps:** `scaffolding-hotspot-complexity-reduction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:51-63`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`

## Acceptance

- [ ] One shared helper computes namespace-relative folder segments for both services.
- [ ] Root namespace, equal namespace, deeper namespace, and unrelated namespace cases are pinned by one regression test file.
- [ ] Both local duplicate bodies are removed without changing output paths.

## Evidence

- Cold scaffolding review found the same namespace-to-folder resolution rule in adjacent services.
## Validation

- Cover both callers in `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs` and `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`.
