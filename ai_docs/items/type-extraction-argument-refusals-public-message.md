# type-extraction-argument-refusals-public-message — extract type/method, record field addition and namespace relocation argument refusals are Public and path-relative

**row:** `type-extraction-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`
- `tests/RoslynMcp.Tests/RecordFieldAdditionImpactTests.cs`

## Acceptance

- [ ] TypeExtractionService.cs:163 no longer republishes a lower-layer message.
- [ ] NamespaceRelocationService.cs:258 names paths relative to the project.
- [ ] ExtractMethodService.cs:44/:511 (paramless) name the start/end parameters.

## Evidence

- 12 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~40000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
