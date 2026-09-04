# scaffold-warning-domain-data — Model incomplete sibling discovery as typed domain data

**row:** `scaffold-warning-domain-data` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingReadFailurePolicy.cs`
- `src/RoslynMcp.Core/Services/IScaffoldingService.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- Return a structured stable warning code and normalized opaque correlation ID from sibling-name collection; do not expose rendered prose as domain state.
- Preserve readable sibling names, single-scan behavior, cancellation propagation, and fail-loud unexpected exceptions.
- Centralize terminal warning rendering in `ScaffoldingReadFailurePolicy`; keep paths, filenames, exception type/message, counts, source, and sentinel values out of the public text.

## Regression

Inject one expected sibling read failure beside one readable sibling, assert typed warning data plus retained names, and render the same redacted terminal warning once without inspecting prose to recover its correlation ID.

## Evidence

PR #1472's cold review found incomplete-discovery state is represented as operator-facing prose, forcing downstream transport code to parse a redaction-sensitive sentence. The domain layer needs a stable typed payload before the MRTR codec can stop carrying presentation text.
