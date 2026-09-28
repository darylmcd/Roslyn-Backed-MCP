# restructure-and-semantic-grep-argument-refusals-public-message — symbol_refactor / restructure / semantic_grep argument refusals are Public

**row:** `restructure-and-semantic-grep-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `src/RoslynMcp.Roslyn/Services/RestructureService.cs`
- `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs`
- `tests/RoslynMcp.Tests/RestructureServiceTests.cs`
- `tests/RoslynMcp.Tests/SemanticGrepServiceTests.cs`

## Acceptance

- [ ] semantic_grep invalid regex keeps today's text via PublicArgumentException (the pattern arm becomes dead).
- [ ] RestructureService.cs:211 no longer echoes pattern text publicly.
- [ ] Paramless sites name a parameter.

## Evidence

- 22 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~45000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
