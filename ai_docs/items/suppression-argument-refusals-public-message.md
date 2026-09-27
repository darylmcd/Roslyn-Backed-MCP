# suppression-argument-refusals-public-message — Pragma/suppression argument refusals (diagnosticId, line, filePath) are Public

**row:** `suppression-argument-refusals-public-message` · **pri:** `Medium` · **size:** `S` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs`
- `tests/RoslynMcp.Tests/PragmaScopeManipulationTests.cs`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs`
- `tests/RoslynMcp.Tests/SuppressionToolsTests.cs`

## Acceptance

- [ ] A pragma suppression with line=0 names 'line' and '1-based' through the envelope.

## Evidence

- 6 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
