# bulk-refactoring-argument-refusals-public-message — bulk replace / replace_invocation / string-literal replace argument refusals are Public without echoing raw signatures

**row:** `bulk-refactoring-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs`
- `tests/RoslynMcp.Tests/ReplaceInvocationTests.cs`
- `tests/RoslynMcp.Tests/StringLiteralReplaceServiceTests.cs`

## Acceptance

- [ ] replace_invocation_preview with a malformed signature names the parameter and expected form.
- [ ] BulkRefactoringService.cs:44 (paramless scope) names 'scope' and the valid values.

## Evidence

- 9 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
