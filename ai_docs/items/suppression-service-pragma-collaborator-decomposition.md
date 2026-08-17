# suppression-service-pragma-collaborator-decomposition — separate pragma parsing and mutation from the suppression facade

## Anchors

- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs`
- New `src/RoslynMcp.Roslyn/Services/PragmaSuppressionService.cs`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs`
- `tests/RoslynMcp.Tests/PragmaScopeManipulationTests.cs`

## Acceptance

- [ ] Extract pragma add, verify, widen, directive scanning, and edit construction behind one internal collaborator; keep `ISuppressionService` and `IPinnedSuppressionWriteService` wire/API behavior unchanged through the existing facade.
- [ ] Leave editorconfig severity mutation and diagnostic fire-site confirmation in focused owners; no duplicate pragma parsing remains in `SuppressionService`.
- [ ] Add one table-driven parity regression covering add idempotency, verify coverage, and widen safety through the facade before and after extraction.
- [ ] Keep the change bounded to at most four production files and three test files.

## Evidence

Observed during the 2026-08-16 suppression root-boundary remediation: `SuppressionService.cs` exceeds 700 lines and owns editorconfig mutation, disk reads, pragma syntax scanning, text-edit construction, widening safety, and live-compilation confirmation. The security fix added only a canonical-write seam; folding this decomposition into it would obscure the boundary invariant and exceed the current row's value.
