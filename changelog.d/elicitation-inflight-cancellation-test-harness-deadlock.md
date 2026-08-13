---
category: Maintenance
---

- **Maintenance:** Resolved the long-standing in-flight elicitation-cancellation test dead end — the hang was `McpClient.DisposeAsync` waiting on a deliberately never-completing test elicitation handler during harness teardown, not `server.ElicitAsync` (which cancels correctly in ~2 ms with the client unresponsive, confirming a stalled client cannot wedge the server or leak its `WorkspaceExecutionGate` slot). Added the previously-untestable genuinely-in-flight cancellation regression test, made the test harness release pending handlers before disposal, and moved `SymbolDisambiguationElicitationTests` to MSTest's cooperative `[Timeout(..., CooperativeCancellation = true)]` shape.
