# MCP Server Best Practices

<!-- purpose: Canonical reference for designing error handling, dispatcher hooks, and protocol hygiene in this repo's MCP server. Cites authoritative sources (spec, Anthropic governance, .NET SDK docs, community guides) and records this repo's applied decisions. Read before proposing changes to Program.cs, ToolErrorHandler, tool-call dispatch, or anything that shapes error envelopes. -->

This document consolidates external best-practice guidance for MCP (Model Context Protocol) servers and records the design decisions this repo applies. Cite this file when reviewing changes that touch the tool-call pipeline, error shapes, or the filter/middleware surface.

Governance changes faster than stable releases; when the evidence links below disagree with this file, trust the linked source and PR an update here.

---

## 1. Spec-level error model

The MCP specification defines **two distinct error channels** for `tools/call`, and they reach different audiences. Getting this distinction right is the single most important design principle in this document.

| Channel | Shape | Who sees it | Use for |
|---|---|---|---|
| **Protocol error** (JSON-RPC `error` object) | `{ "jsonrpc": "2.0", "id": N, "error": { "code": -326xx, "message": "..." } }` | The **MCP client** (Claude Code, Cursor, etc.) — stripped before the model sees it | Unknown tool name; malformed JSON-RPC envelope; transport-level faults; truly fatal server state |
| **Tool-execution error** (`CallToolResult` with `isError: true`) | `{ "content": [{ "type": "text", "text": "..." }], "isError": true }` | The **LLM** — surfaced into its context window | Argument validation failures; business-logic failures; downstream API errors; anything the model needs to self-correct from |

Reference: [MCP spec — tools](https://modelcontextprotocol.io/specification/2025-06-18/server/tools). Standard JSON-RPC codes: `-32700` parse, `-32600` invalid request, `-32601` method not found, `-32602` invalid params, `-32603` internal, `-32000..-32099` implementation-specific ([codes reference](https://www.mcpevals.io/blog/mcp-error-codes)).

### 1.1 Governance direction: binding errors → tool-execution errors

[MCP SEP-1303](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1303) (accepted; reference implementation merged) settles the ambiguous case — **input validation errors should be returned as `CallToolResult { isError: true }`, not as JSON-RPC `-32602`**. Direct quote:

> "Language models can learn from tool input validation error messages and retry a tools/call with corrected parameters accordingly, but only if they receive the error feedback in their context window."

Concretely: if a caller omits a required parameter or supplies an unknown parameter name, the MCP client hides `-32602` protocol errors from the LLM. The LLM retries blind. Returning the same problem as `isError: true` feeds the diagnosis into the model's next turn and closes the loop.

**This repo's rule:** everything the LLM could plausibly correct on retry goes through the tool-execution channel. Reserve protocol errors for problems the human operator or client must fix.

---

## 2. .NET SDK native pattern: request filters

This repo pins `ModelContextProtocol` 1.1.0. The SDK exposes a first-class decorator/middleware pipeline for each MCP method:

- Entrypoint: `IMcpServerBuilder.WithRequestFilters(Action<IMcpRequestFilterBuilder>)`
- Tool-call slot: `IMcpRequestFilterBuilder.AddCallToolFilter(McpRequestFilter<CallToolRequestParams, CallToolResult>)`
- Delegate shape: `next => (context, ct) => ...` — the filter receives the next handler in the pipeline and returns a wrapped handler. Multiple filters compose.

Canonical example from the [Microsoft SDK filters docs](https://csharp.sdk.modelcontextprotocol.io/concepts/filters.html):

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "An unexpected error occurred..." }],
                IsError = true
            };
        }
    });
});
```

### 2.1 Filters see pre-binding exceptions (as of SDK 0.4.0-preview.3+)

The SDK originally swallowed reflection-binding exceptions inside the invocation wrapper, returning a bare `"An error occurred invoking '<tool>'."` string. That defect is tracked in [csharp-sdk#820](https://github.com/modelcontextprotocol/csharp-sdk/issues/820) and [csharp-sdk#830](https://github.com/modelcontextprotocol/csharp-sdk/issues/830), and fixed in [PR #844 "Propagate tool call exceptions through filters"](https://github.com/modelcontextprotocol/csharp-sdk/pull/844) (shipped 0.4.0-preview.3, carried into 1.x). On 1.1.0+, filters observe:

- `ArgumentException` / `ArgumentNullException` from parameter binding (missing or unknown required argument)
- `JsonException` from `arguments` deserialization
- `FormatException` from per-parameter conversion
- Any exception thrown inside the tool body itself

This means a single `AddCallToolFilter` is the **only place in the repo** that needs to know about exceptions during tool calls. Per-handler wrappers become redundant.

### 2.2 Why filters, not generic middleware

The SDK maintainers [explicitly chose filters over a generic middleware abstraction](https://github.com/modelcontextprotocol/csharp-sdk/issues/267) to preserve protocol fidelity across SDKs:

> "Someone should be able to port an MCP server from another SDK to this one without running into problems with parts of the protocol being entirely obscured by abstractions."

Don't invent middleware; use the filter slots the SDK provides (`AddCallToolFilter`, `AddListToolsFilter`, `AddReadResourceFilter`, etc.).

---

## 3. Where error handling belongs in this repo

| Concern | Location | Why |
|---|---|---|
| Pre-binding / binding / post-binding exception classification | A single `AddCallToolFilter` in [Program.cs](../../src/RoslynMcp.Host.Stdio/Program.cs) — see § 2 above | One boundary for the full registered tool surface; covers every failure mode |
| Error taxonomy (`InvalidArgument`, `NotFound`, `WorkspaceReloadedDuringCall`, `InternalError`, `RateLimited`, `Timeout`, ...) | [`ToolErrorHandler.ClassifyError`](../../src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs) | Pure classification; reused by filter + resource wrappers |
| Response envelope shape (`error: true, category, tool, message, exceptionType`) | `ToolErrorHandler.FormatErrorResponse` | Shared shape across tool-execution errors and resource errors |
| Metrics / `_meta` injection | The same filter (cross-cutting, not per-tool) | Prevents per-tool duplication; guarantees coverage |
| Validation of domain inputs (file paths, symbol handles, etc.) | Inside the tool body, throwing typed exceptions that the filter classifies | Keeps business logic local; the filter provides the envelope |

**Anti-pattern:** wrapping tool bodies with `ToolErrorHandler.ExecuteAsync(...)` as the primary error boundary. This is the pre-filter legacy pattern that misses pre-binding failures entirely. New tools must not adopt it; existing usages should migrate to the filter.

---

## 4. Anti-patterns (with citations)

### 4.1 Swallowing exceptions inside tool bodies

Bare `catch (Exception) { return ""; }` or "log and return null" inside tool methods hides information the LLM needs to self-correct. Let exceptions propagate to the filter, which owns the envelope. Guidance from the [Microsoft SDK docs on `McpServerTool`](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerTool.html):

> "When a tool throws an `McpException`, its Message is included in the error result sent to the client. Throwing any other exception type also results in an error `CallToolResult`, but with a generic error message (to avoid leaking sensitive information). Alternatively, a tool can declare a return type of `CallToolResult` to have full control over both success and error responses. The tool method is responsible for validating its own input arguments."

Domain validation at the top of a tool body is fine (and expected) — just let the thrown exception reach the filter.

### 4.2 Using JSON-RPC protocol errors for recoverable problems

Anything the LLM could fix on retry belongs in `CallToolResult { isError: true }`. Reserve `McpException` / JSON-RPC errors for:

- Unknown tool name (spec mandates `-32601`-class handling by the SDK)
- Malformed JSON-RPC envelope
- Transport-level faults the client must handle

### 4.3 Cross-cutting concerns in tool bodies under HTTP transport

From the [.NET SDK v1.0 release blog](https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/):

> "The MCP HTTP handler may flush response headers before invoking the tool. By the time the tool call method is invoked, it is too late to set the response status code or headers."

Authorization, rate-limiting, metrics, request-body transformation → **filter or transport layer**, never the tool body. Applies to stdio too by convention; this repo runs stdio-only today but the rule still stands.

### 4.4 Logging to stdout on stdio transport

Stdio-transport MCP servers **must not** write to stdout except framed JSON-RPC messages. Any other byte corrupts the client's parser. All logging, traces, and diagnostic output go to stderr. Enforced in this repo by the `McpLoggingProvider` bridge + [Program.cs](../../src/RoslynMcp.Host.Stdio/Program.cs) wiring; see the [Stainless guide](https://www.stainless.com/mcp/error-handling-and-debugging-mcp-servers) for background.

---

## 5. Observability and message discipline

- **Log full detail server-side, return sanitized text to the client.** [mcpcat guide](https://mcpcat.io/guides/error-handling-custom-mcp-servers/): "Log Internally... Sanitize Responses: Return user-safe messages that don't leak system information." This repo's `ClassifyError` already embeds actionable hints (e.g. "Call `workspace_reload` if state is stale") without leaking paths or stack internals in non-`InternalError` categories.
- **Include a `_meta` block** on every response carrying gate-metrics snapshot (queue time, hold time, elapsed, stale-reload timing). Enables concurrency audits from inside the agent loop. The filter opens the scope and calls `ToolErrorHandler.InjectMetaIfPossible` on both success and error paths.
- **Emit MCP `notifications/message` for startup and workspace-lifecycle events** (see `McpLoggingProvider`). Clients that surface structured logs (Claude Code) see the events proactively; clients that don't still get them on the next tool call via `_meta`.

---

## 6. Schema and tool-surface hygiene

- **Every public parameter must carry `[Description(...)]`.** Agents introspect these via `ToolSearch` / `tools/list`; undocumented parameters lead to the exact failure mode that triggered this doc's creation.
- **Prefer required non-nullable types over optional-with-sentinel-null** for parameters the tool truly cannot proceed without. The filter will surface a clean `InvalidArgument` envelope naming the parameter when it's missing.
- **Read-only annotations** (`[McpServerTool(ReadOnly = true)]`) matter — clients gate what a tool can do based on these. Get them right.
- **DI-register services against the interface that tool methods inject**, not only the concrete type. [`di-register-latest-version-provider`](../backlog.md) recorded the failure mode: if the SDK's parameter binder can't resolve a service-typed parameter, it leaks that parameter into the tool schema as a required user-supplied argument.

---

## 7. Applied decisions in this repo

This section is the live log of decisions derived from the above principles. Update with PR numbers on ship.

| Decision | Source principle | Implementation |
|---|---|---|
| Single `AddCallToolFilter` is the sole error boundary for tool calls | § 1.1, § 2.1, § 3 | [`StructuredCallToolFilter`](../../src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs) registered in [`Program.cs`](../../src/RoslynMcp.Host.Stdio/Program.cs) via `WithRequestFilters(b => b.AddCallToolFilter(...))`. Opens the `AmbientGateMetrics` scope, classifies every exception through `ToolErrorHandler.ClassifyAndFormat`, and returns `CallToolResult { IsError = true, Content = [TextContentBlock { Text = envelope }] }` on failure. Filter-level regressions in [`StructuredCallToolFilterTests`](../../tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs). |
| Per-handler `ToolErrorHandler.ExecuteAsync` wrapper is retired after the filter lands | § 3 anti-pattern | Swept from all 50 tool files in `src/RoslynMcp.Host.Stdio/Tools/` (174 call sites removed); `ToolErrorHandler.ExecuteAsync` method deleted. Unit tests that exercised the classifier through the legacy wrapper now call [`ToolExecutionTestHarness.RunAsync`](../../tests/RoslynMcp.Tests/Helpers/ToolExecutionTestHarness.cs) — an in-process mirror of the filter's control flow that lives only in the test project. |
| `ClassifyError` handles pre-binding failures in BOTH shapes: wrapped in `TargetInvocationException` / `InvalidOperationException` AND raw-unwrapped as the SDK filter pipeline delivers them | § 4.1 | [`ToolErrorHandler.TryClassifyBindingLike`](../../src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs) — single helper invoked against both the inner exception (wrapped) and the exception itself (unwrapped). Resolves [csharp-sdk#830](https://github.com/modelcontextprotocol/csharp-sdk/issues/830) for this repo's filter path. Regression coverage: [`ToolErrorHandlerParameterValidationTests`](../../tests/RoslynMcp.Tests/ToolErrorHandlerParameterValidationTests.cs), [`StructuredCallToolFilterTests.BuildErrorResult_MissingRequiredParameter_*`](../../tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs). |
| `_meta` block on every response with gate-metrics snapshot | § 5 | The filter opens the [`AmbientGateMetrics`](../../src/RoslynMcp.Core/Services/AmbientGateMetrics.cs) scope on entry and injects the snapshot via [`ToolErrorHandler.InjectMetaIfPossible`](../../src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs) on both success (into the first `TextContentBlock` of the returned `CallToolResult`) and error (into the envelope text). Array-rooted responses pass through unchanged — see `StructuredCallToolFilter.InjectMetaIntoContent`. |
| Stdio transport writes framed JSON-RPC only; logging via `McpLoggingProvider` to stderr | § 4.4 | [Program.cs](../../src/RoslynMcp.Host.Stdio/Program.cs), `McpLoggingProvider` |
| Services registered against interface + concrete type for SDK binder compatibility | § 6 | [Program.cs:47-49](../../src/RoslynMcp.Host.Stdio/Program.cs) with `di-register-latest-version-provider` comment |

---

## 8. Sources

### Spec and governance
- [MCP spec — tools (2025-06-18)](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)
- [MCP SEP-1303: Input validation errors as tool execution errors](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1303)
- [MCP error codes reference](https://www.mcpevals.io/blog/mcp-error-codes)

### .NET SDK
- [`csharp.sdk.modelcontextprotocol.io` — Filters concept](https://csharp.sdk.modelcontextprotocol.io/concepts/filters.html)
- [`csharp.sdk.modelcontextprotocol.io` — `McpServerTool` API](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerTool.html)
- [Microsoft DevBlogs: Release v1.0 of the official MCP C# SDK](https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/)
- [csharp-sdk#267 — Middleware design rationale](https://github.com/modelcontextprotocol/csharp-sdk/issues/267)
- [csharp-sdk#820 — AddCallToolFilter does not catch exceptions (defect)](https://github.com/modelcontextprotocol/csharp-sdk/issues/820)
- [csharp-sdk#830 — ArgumentException from parameter marshalling (defect)](https://github.com/modelcontextprotocol/csharp-sdk/issues/830)
- [csharp-sdk#844 — Fix: propagate tool call exceptions through filters](https://github.com/modelcontextprotocol/csharp-sdk/pull/844)

### Community patterns
- [mcpcat.io — Error Handling in Custom MCP Servers](https://mcpcat.io/guides/error-handling-custom-mcp-servers/)
- [Stainless — Error Handling and Debugging MCP Servers](https://www.stainless.com/mcp/error-handling-and-debugging-mcp-servers)
- [cyanheads — MCP Server Development Guide](https://github.com/cyanheads/model-context-protocol-resources/blob/main/guides/mcp-server-development-guide.md)

### Transport-specific
- [MCP spec — transports](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports) — stdio framing rules
