# roslyn-internal-argument-guards-not-caller-errors — Roslyn internal invariants (validation switch default, watcher reason, runner executable, undo solution type) stop throwing ArgumentException

**row:** `roslyn-internal-argument-guards-not-caller-errors` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] WorkspaceValidationService.cs:616 throws UnreachableException.
- [ ] FileWatcherService.cs:139, DotnetCommandRunner.cs:162 and UndoService.cs:75 use ThrowIf helpers or InvalidOperationException.

## Evidence

- 4 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~25000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
