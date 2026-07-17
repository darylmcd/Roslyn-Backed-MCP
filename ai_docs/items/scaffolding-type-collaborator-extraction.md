# scaffolding-type-collaborator-extraction — Extract type-scaffolding collaborator

**row:** `scaffolding-type-collaborator-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:23-40`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TypePreview.cs:12-350`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] `ScaffoldingService` remains the `IScaffoldingService` facade and `PreviewScaffoldTypeAsync` delegates to one type-scaffolding collaborator.
- [ ] The collaborator owns interface resolution, type rendering, and `InterfaceResolutionResult`; the facade constructor, public interface, and DI lifetime remain unchanged.
- [ ] Type preview content, preview tokens, warnings, and failure behavior remain byte-shape compatible.
- [ ] Type, single-test, and batch-test scaffolding regressions pass.

## Evidence

- Execute-time cold review of `20260716T165737Z_backlog-sweep` found the original god-class row contained multiple independently testable regressions.
- Roslyn read-side inspection loaded `RoslynMcp.slnx` and confirmed the partial type and anchors on 2026-07-17.

## Dependencies

- None. This is the first stage of the scaffolding facade split.
