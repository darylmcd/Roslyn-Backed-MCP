# code-action-and-flow-argument-refusals-public-message — code action / fix-all scope / flow-analysis range argument refusals are Public

**row:** `code-action-and-flow-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`
- `src/RoslynMcp.Roslyn/Services/FixAllTargetResolver.cs`
- `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs`
- `tests/RoslynMcp.Tests/FlowAnalysisServiceTests.cs`
- `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] CodeActionService.cs:174 (paramless) names actionIndex and the available count.
- [ ] FlowAnalysisService catch/rethrow (:38, :96) preserves Public messages.
- [ ] ExpandedSurfaceIntegrationTests.cs (pins :632-:694) is shared with argument-exception-throw-sites-lack-public-message.

## Evidence

- 9 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
