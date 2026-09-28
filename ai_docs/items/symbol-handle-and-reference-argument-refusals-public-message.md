# symbol-handle-and-reference-argument-refusals-public-message — symbolHandle decode, bulk-locator, relationship projectName and type-consumer argument refusals are Public

**row:** `symbol-handle-and-reference-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs`
- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs`
- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs`
- `tests/RoslynMcp.Tests/NegativeEdgeCaseTests.cs`
- `tests/RoslynMcp.Tests/DocumentSymbolsSymbolHandleTests.cs`

## Acceptance

- [ ] A malformed symbolHandle names 'symbolHandle' and the failure kind (base64/JSON) through the envelope.
- [ ] ReferenceService.cs:564 (paramless 50-symbol cap) names the parameter.

## Evidence

- 10 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
