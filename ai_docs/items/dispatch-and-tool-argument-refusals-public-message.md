# dispatch-and-tool-argument-refusals-public-message — Dispatch/elicitation/compile_check/edit argument refusals (workspace discovery, required path, projectName miss, ambiguous path) are Public and path-free

**row:** `dispatch-and-tool-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/EditTools.cs`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallElicitationCoordinatorTests.cs`
- `tests/RoslynMcp.Tests/CompileCheckZeroProjectsTests.cs`

## Acceptance

- [ ] compile_check with an unknown projectName returns the Public message.
- [ ] The EditTools ambiguous-path refusal does not embed an absolute path.
- [ ] The workspace auto-discovery refusal is Public (the workspaceId candidate arm becomes dead).

## Evidence

- 4 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
