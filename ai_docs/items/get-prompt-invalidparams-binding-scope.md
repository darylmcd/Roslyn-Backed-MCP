## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/GetPromptErrorFilter.cs:117` — the InvalidParams branch
- `tests/RoslynMcp.Tests/PromptCallErrorFilterTests.cs`

## Acceptance

- An `ArgumentException` raised inside a prompt HANDLER no longer surfaces its raw `.Message` to the client through the InvalidParams branch; only true parameter-binding failures map to InvalidParams.
- Handler-raised exceptions route to the sanitized internal-error path with a correlation id.
- Regression test exercising a handler-thrown `ArgumentException` (distinct from the existing binding-failure test).

## Evidence

Traced in code during PR #1284 review. `GetPromptErrorFilter.Create` wraps the full dispatcher (binding + handler) and `TranslateException` switches only on exception TYPE, so an `ArgumentException` raised anywhere inside a prompt handler reaches line 117 and has its raw `.Message` interpolated into the returned `McpProtocolException`. This partially re-opens the detail-disclosure hole the initiative closed, on the handler path. No existing test covers it.

Source: code-quality review of PR #1284 (initiative `prompt-call-error-filter-boundary`, sweep 20260819T180531Z).
