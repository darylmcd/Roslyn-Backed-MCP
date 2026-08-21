# workspace-lifecycle-handler-log-redaction — redact lifecycle subscriber failures

**row:** `workspace-lifecycle-handler-log-redaction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` — `RaiseWorkspaceLifecycleEvent`.
- `tests/RoslynMcp.Tests/WorkspaceReloadedEventTests.cs`

## Acceptance

- [ ] Preserve per-subscriber isolation: a throwing close/reload handler must not prevent later handlers from running.
- [ ] Log only the lifecycle event name, workspace id, and exception type or correlation-safe structure; do not attach the raw exception object or emit its message/stack.
- [ ] One table-driven close/reload regression injects a sentinel message and absolute path, proves later-handler execution, and asserts the logger exception is null and the sentinel/path are absent.

## Evidence

`WorkspaceManager.RaiseWorkspaceLifecycleEvent` deliberately catches each subscriber failure so later handlers still run, but passes the raw subscriber exception to `ILogger.LogWarning`. Subscriber-controlled messages and stacks can therefore disclose local paths or secrets through the server diagnostic channel.
