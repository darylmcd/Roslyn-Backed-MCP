# refactoring-service-plain-caller-refusals — Classify RefactoringService plain InvalidOperationException refusals as public caller corrections or Internal

**row:** `refactoring-service-plain-caller-refusals` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:64,150` (duplicated metadata-symbol rename refusal)
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:461,463,465,661,671,731,737,741,759`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Every caller-correctable refusal is a PublicInvalidOperationException with path/input-free guidance; true invariants use InvalidOperationErrors.Internal; the duplicated refusal is one helper.
- [ ] Red-first service and wire tests with hostile sentinels; the ratchet baseline only shrinks.

## Evidence

- Found while reviewing the change-signature and format-range refusal PR. :671 interpolates filePath:line:column into a plain exception; redaction relies on the host fallback.
