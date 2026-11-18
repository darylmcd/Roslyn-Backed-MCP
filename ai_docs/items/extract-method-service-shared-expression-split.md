# extract-method-service-shared-expression-split — Move the shared-expression pipeline out of ExtractMethodService

**row:** `extract-method-service-shared-expression-split` · **pri:** `Low` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- (new) `src/RoslynMcp.Roslyn/Services/SharedExpressionExtractionService.cs`

## Acceptance

- [ ] The shared-expression preview lives in its own service registered in DI; ExtractMethodService keeps extract_method only; public tool behaviour and messages unchanged (surface/catalog tests green).

## Evidence

- shared-expr report (PR 2142): "ExtractMethodService.cs is a god class; the shared-expression pipeline merits its own service." Verified at 12bc2c8b: 1077 lines.
