# scaffolding-batch-first-test-collaborator-extraction — Extract batch and first-test scaffolding collaborator

**row:** `scaffolding-batch-first-test-collaborator-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs:12-749`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] `PreviewScaffoldTestBatchAsync` and `PreviewScaffoldFirstTestFileAsync` delegate from the unchanged facade to one batch/first-test collaborator.
- [ ] The collaborator owns batch state/context and directly consumes the shared support established by the prerequisite; no collaborator dependency cycle exists.
- [ ] The current one-compilation-per-project batch cache remains one compilation per project.
- [ ] Preview content, warnings, tokens, facade constructor, public interface, and DI lifetime remain unchanged.

## Evidence

- Execute-time cold review identified batch/first-test orchestration and caching as a separate regression shape.

## Dependencies

- `scaffolding-single-test-collaborator-extraction`
