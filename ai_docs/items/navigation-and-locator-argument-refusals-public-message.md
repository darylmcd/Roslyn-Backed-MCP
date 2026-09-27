# navigation-and-locator-argument-refusals-public-message — Position bounds (line/column) in navigation, resolver and completion plus Core SymbolLocator

**row:** `navigation-and-locator-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs`
- `src/RoslynMcp.Roslyn/Services/CompletionService.cs`
- `src/RoslynMcp.Core/Models/SymbolLocator.cs`
- `tests/RoslynMcp.Tests/PositionProbeTests.cs`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`
- `tests/RoslynMcp.Tests/SymbolLocatorTests.cs`

## Acceptance

- [ ] go_to_definition(line=99999) and enclosing_symbol(line=0) name 'line' and the file's line count through the envelope.
- [ ] SymbolResolver.cs:89 uses a ThrowIf helper.
- [ ] SymbolLocatorTests.cs is shared with symbol-locator-and-source-text-tool-argument-refusals-public-message.

## Evidence

- 8 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~40000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
