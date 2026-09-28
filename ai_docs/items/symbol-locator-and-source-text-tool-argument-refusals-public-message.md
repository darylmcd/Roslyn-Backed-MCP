# symbol-locator-and-source-text-tool-argument-refusals-public-message — Host locator/source-text/validation/analysis tool argument refusals (incomplete source location, line ranges, maxChars, limits, alias conflicts) are Public

**row:** `symbol-locator-and-source-text-tool-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SourceTextRequestProjection.cs`
- `tests/RoslynMcp.Tests/SymbolLocatorTests.cs`
- `tests/RoslynMcp.Tests/SourceTextRequestProjectionTests.cs`
- `tests/RoslynMcp.Tests/AnalysisToolsTests.cs`

## Acceptance

- [ ] An incomplete source location names the missing coordinates through the envelope (the filePath+column arm becomes dead).
- [ ] A startLine past EOF names the line count (the startLine arm becomes dead).
- [ ] SymbolLocatorTests.cs is shared with navigation-and-locator-argument-refusals-public-message.

## Evidence

- 14 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~40000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
