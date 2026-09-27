# build-and-analysis-argument-refusals-public-message — MSBuild evaluation, related-tests cap, unused usageKind and dead-code argument refusals are Public

**row:** `build-and-analysis-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs`
- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`
- `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs`
- `tests/RoslynMcp.Tests/MsBuildEvaluationServiceTests.cs`
- `tests/RoslynMcp.Tests/DeadCodeIntegrationTests.cs`
- `tests/RoslynMcp.Tests/HighValueCoverageIntegrationTests.cs`

## Acceptance

- [ ] evaluate_msbuild_property without project returns the Public example-bearing message.
- [ ] find_unused with a bad usageKind lists valid values without echoing input.

## Evidence

- 4 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
