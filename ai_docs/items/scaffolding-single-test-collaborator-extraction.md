# scaffolding-single-test-collaborator-extraction — Extract single-test scaffolding collaborator

**row:** `scaffolding-single-test-collaborator-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs:12-1099`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] `PreviewScaffoldTestAsync` delegates from the unchanged facade to one single-test collaborator.
- [ ] The collaborator owns single-test resolution, sibling inference, and sampled-name selection.
- [ ] Shared test-rendering support owns target resolution, constructor arguments, framework rendering, and project validation; batch code consumes that support directly and never calls the single-test collaborator.
- [ ] Logger categories/messages, preview content, warnings, tokens, facade constructor, public interface, and DI lifetime remain unchanged.

## Evidence

- Execute-time cold review split the single-test workflow from the type and batch/first-test regression shapes.

## Dependencies

- `scaffolding-type-collaborator-extraction`
