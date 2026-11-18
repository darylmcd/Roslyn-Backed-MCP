# interface-conflict-check-plain-ioe-first-project — Name the owning project in the extract_interface conflict refusal

**row:** `interface-conflict-check-plain-ioe-first-project` · **pri:** `Low` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`:267-296 — `ValidateNoConflictsAsync` throws `InvalidOperationException` for the first project whose compilation resolves the name.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`:476 — twin throws `PublicInvalidOperationException`.
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json` — ratchet entry for `ValidateNoConflictsAsync`.

## Acceptance

- [ ] The refusal names the project that declares the conflicting type (its containing assembly), not the first project that can see it through a reference.
- [ ] Both routes throw the same public refusal type; the ratchet baseline entry is removed; changelog Migration note for the exception-type change.

## Evidence

- iface-extraction-b report (PR 2148), sketch 3 and the later re-sighting: plain `InvalidOperationException` ("already exists in project ...") named `SampleApp` although the type lives in SampleLib, while the cross-project twin throws `PublicInvalidOperationException`.
- Verified at 12bc2c8b: InterfaceExtractionService.cs:291-295 and CrossProjectRefactoringService.cs:476.

## Context

- Release gate is the same as `extract-type-partial-refusal-message-and-dup-loops`: turning a ratcheted plain exception into a public message is held for the next major.
