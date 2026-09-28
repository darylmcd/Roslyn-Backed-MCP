# argument-errors-redacted-factory — Core ArgumentErrors

**row:** `argument-errors-redacted-factory` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Core/Services/ArgumentErrors.cs`
- `tests/RoslynMcp.Tests/ArgumentErrorsTests.cs`

## Acceptance

- [ ] Redacted returns GetType()==typeof(ArgumentException) with ParamName set; blank parameterName throws.
- [ ] Through ToolErrorHandler.ClassifyAndFormat the envelope shows the generic 'Parameter <name> is invalid' text, never serverDetail.

## Evidence

- 0 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~20000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
