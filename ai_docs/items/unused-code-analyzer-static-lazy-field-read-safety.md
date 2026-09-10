# unused-code-analyzer-static-lazy-field-read-safety — unused-code-analyzer-static-lazy-field-read-safety

**row:** `unused-code-analyzer-static-lazy-field-read-safety` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/PromptParameterIndex.cs`
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs`

## Acceptance

- [ ] Classify reads through `Lazy<T>.Value` correctly and never label a field with a semantic read as `safelyRemovable`.
- [ ] Preserve existing read/write classification for non-Lazy fields.
- [ ] Add one static Lazy-backed index regression that is absent from the never-read result.

## Evidence

- `find_dead_fields` called `PromptParameterIndex.s_index` never-read and safely removable while exact `find_references` found its `.Value` read.
