# host-config-argument-guards-not-caller-errors — Env/config validation in Host startup and the workspace_close drain seam stop throwing ArgumentException

**row:** `host-config-argument-guards-not-caller-errors` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Host.Stdio/HostEnvironmentOptions.cs`
- `src/RoslynMcp.Host.Stdio/Diagnostics/ServerObservability.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Invalid ROSLYNMCP_ON_STALE / observability env values fail startup with an actionable InvalidOperationException.
- [ ] WorkspaceTools.cs:153 is coordinated with workspace-close-schema-leaks-test-seams (same file).

## Evidence

- 3 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
