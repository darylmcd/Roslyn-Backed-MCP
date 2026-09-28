# workspace-lifecycle-argument-refusals-public-message — workspace_load path / gate workspaceId / physical-path argument refusals are Public and path-free

**row:** `workspace-lifecycle-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs`
- `tests/RoslynMcp.Tests/WorkspaceIdOptionalSurfaceTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadDedupTests.cs`

## Acceptance

- [ ] workspace_load with a non-solution/non-project path says it must be a solution (sln, slnx) or C# project (csproj) file, without echoing the path.
- [ ] WorkspaceManager.cs is a hotspot: schedule it alone in its wave.

## Evidence

- 6 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
