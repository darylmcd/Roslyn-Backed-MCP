# scripting-and-snippet-argument-refusals-public-message — evaluate_csharp timeout-override/IPC-size and analyze_snippet kind refusals are Public

**row:** `scripting-and-snippet-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptWorkerProcess.cs`
- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs`
- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs`
- `tests/RoslynMcp.Tests/SnippetAnalysisKindTests.cs`

## Acceptance

- [ ] analyze_snippet{kind:'bogusKind'} names 'kind' and the 5 valid kinds without echoing input (moved from core-move).
- [ ] An over-cap timeoutSecondsOverride names the parameter and the cap.
- [ ] ScriptExecutionSupervisor.cs:341-345 throws internal/config exceptions.

## Evidence

- 7 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
