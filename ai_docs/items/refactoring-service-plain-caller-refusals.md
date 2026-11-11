# refactoring-service-plain-caller-refusals — Classify RefactoringService plain InvalidOperationException refusals as public caller corrections or Internal

**row:** `refactoring-service-plain-caller-refusals` · **pri:** `Defer` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:64,150` (duplicated metadata-symbol rename refusal)
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:461,463,465,661,671,731,737,741,759`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Every caller-correctable refusal is a PublicInvalidOperationException with path/input-free guidance; true invariants use InvalidOperationErrors.Internal; the duplicated refusal is one helper.
- [ ] Red-first service and wire tests with hostile sentinels; the ratchet baseline only shrinks.

## Evidence

- Found while reviewing the change-signature and format-range refusal PR. :671 interpolates filePath:line:column into a plain exception; redaction relies on the host fallback.

## Notes

5.x remediation screen (2026-11-11, base 52f2c418): RefactoringService.cs:64 and :150 share the metadata/built-in rename refusal. The former serves stable rename_preview; ToolErrorHandler currently replaces its plain exception message with generic InvalidOperation guidance. The complete acceptance would publish a different message for the same stable input, even if category and exceptionType stayed unchanged. Preserve the current message throughout 5.x. Resume at the next operator-approved major with an ADR, migration note and both protocol eras on the raw wire; then re-check all remaining refusal sites and the construction ratchet.
