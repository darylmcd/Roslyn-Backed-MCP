# preview-token-argument-refusals-public-message — Preview-token/fork retention argument refusals are Public without echoing tokens or workspace ids

**row:** `preview-token-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `tests/RoslynMcp.Tests/PreviewApplyBoundaryRevalidationTests.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`
- `tests/RoslynMcp.Tests/PreviewStoreTests.cs`

## Acceptance

- [ ] A cross-workspace previewToken refusal names 'previewToken' without echoing ids.
- [ ] The retention refusal lists valid values.

## Evidence

- 3 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
