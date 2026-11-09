# timeout-request-timer-envelope — Identify request-gate timeout on the wire

**row:** `timeout-request-timer-envelope` · **pri:** `Medium` · **size:** `M`

## Anchors

- (new) `src/RoslynMcp.Core/Services/OperationTimeoutException.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:126`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:622`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:152`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:348`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs:30`

## Acceptance

- [ ] A Core timeout type carries the timer identity, environment knob, configured duration, and elapsed duration without caller input or paths; it derives from `TimeoutException` and preserves the inner cancellation exception.
- [ ] Both request-gate timeout sites throw it with `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS`; a plain `TimeoutException` receives a generic, knob-free message.
- [ ] `ToolErrorHandler` renders only safe typed fields and emits category `Timeout`, existing `isError`, and `exceptionType: TimeoutException` for both typed and plain timeouts.
- [ ] A red-first wire regression proves a gate timeout on `test_run` names only the request knob, never the build/test knobs, and retains the released wire exception type; a plain timeout proves the generic fallback.

## Evidence

- Current `ToolErrorHandler.cs:126-128` overwrites a request timeout's specific knob with generic build/test/request advice; `WorkspaceExecutionGate.cs:152,348` owns the fired request timer. The existing wire test checks only that the request knob appears, so it misses the misleading extra knobs.

## Context

- First independently shippable slice of `timeout-error-names-fired-timer`. Command and restore timers remain tracked by the dependent row. Current release line is 5.0.1; preserve the released category and wire type.
