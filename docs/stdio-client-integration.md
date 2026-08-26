# Direct stdio client integration

This doc exists for orchestrators that spawn `roslynmcp` as a child process and speak MCP directly, rather than through a pre-configured Claude Code / Cursor integration.

If you're using Claude Code (via the `/plugin install roslyn-mcp@roslyn-mcp-marketplace` flow) or Cursor's MCP config UI, you don't need this doc — the client handles the protocol for you. Read on only if you're writing your own harness.

## Protocol at a glance

- **Transport:** NDJSON (newline-delimited JSON) on stdin/stdout. Each message is a single JSON object followed by `\n`. **Do not** use LSP-style `Content-Length:` framing — the server ignores headers and treats the first non-JSON byte as a parse error.
- **Stderr** is for operational logging. Do not parse it as protocol traffic.
- **Spec:** [Model Context Protocol](https://modelcontextprotocol.io). The server implements the stdio transport; the public MCP SDKs speak it natively.
- **Protocol eras:** MCP SDK 2.2 serves modern `2026-07-28` requests and initialize-capable `2025-11-25` and earlier clients. Prefer an official SDK so discovery and fallback remain correct.

## Connection bootstrap

### Modern `2026-07-28`

1. **Spawn** `roslynmcp`.
2. Optionally send `server/discover` with `_meta.io.modelcontextprotocol/protocolVersion`, client identity, and client capabilities. On stdio, dual-era clients should use this as the compatibility probe.
3. Include the negotiated protocol version, client identity, and capabilities in `_meta` on every request. There is no `initialize` / `notifications/initialized` handshake.

The official C# SDK performs this discovery-first negotiation automatically and falls back to a legacy handshake when the server does not support the modern era.

### Initialize-capable compatibility era

1. **Spawn** `roslynmcp` (installed globally via `dotnet tool install -g Darylmcd.RoslynMcp`, CLI command is `roslynmcp`).
2. Send **`initialize`** request — capability negotiation. Response arrives on stdout with a matching `id`.
3. Send **`notifications/initialized`** — a notification (no `id`) telling the server the handshake is done. **Until this arrives, every `tools/call` will be rejected** with "Server not initialized."
4. Send any tool calls. The server accepts them after step 3.

### Response correlation

RoslynMcp emits no protocol logging frames and workspace load/reload/close do not claim that the static resource catalog changed. Concurrent clients must still correlate responses by request `id`; operational logs remain on stderr. See [Operator observability](#operator-observability) for diagnostic sinks and correlation identifiers.

## Operator observability

`stdout` is reserved for MCP frames. The normal enabled `ILogger` stream goes to `stderr` with UTC
timestamps and request scopes. Configure verbosity with the standard .NET environment keys:
`Logging__LogLevel__Default=Debug` changes the default, while a category override such as
`Logging__LogLevel__RoslynMcp=Debug` narrows the additional volume.

`ROSLYNMCP_OBSERVABILITY_SINK` adds an operator-controlled diagnostic projection without changing
the MCP protocol surface:

| Value | Additional output |
|---|---|
| `disabled` (default) | None. Normal operational logs still go to `stderr`. |
| `stderr` | Secret-safe JSON for unexpected failures is added to `stderr`. |
| `file` | The full enabled `ILogger` stream is written as JSON lines under the per-user metadata root's `logs/` directory. Each process owns a 5 MiB current file plus one rotated file. |

The file path is independent of the process working directory:

| Platform/configuration | Log directory |
|---|---|
| Windows default | `%LOCALAPPDATA%/roslyn-mcp/logs/` |
| `XDG_STATE_HOME` set | `$XDG_STATE_HOME/roslyn-mcp/logs/` |
| Other fallback | The platform `LocalApplicationData/roslyn-mcp/logs/` directory; the system temp directory is the last resort when no local-application-data path exists. |

JSON-lines records contain `ts`, `level`, `category`, `eventId`, `message`, and `correlationId`.
Tool-call completion and failure records carry an outcome and elapsed milliseconds. The same request
correlation identifier appears in public unexpected-error envelopes, so an operator can join a
client-visible failure to local records without exposing exception messages or stack text.

Use `server_heartbeat` for a cheap connection-state probe and `server_info` for connection state,
version, capabilities, and surface counts. Neither requires a loaded workspace.

## Parameter naming

Every tool-call parameter uses the exact names shown in `tools/list`. A few that tend to trip up custom clients:

- **`workspace_load`** takes `path` (not `solutionPath` / `sln` / `project`).
- Every workspace-scoped tool takes `workspaceId` — the value returned by the `workspace_load` response. Reuse it for the entire session.
- JSON keys are camelCase. Response keys match (e.g. `workspaceId`, `previewToken`, not `WorkspaceId`).

## Minimal legacy Python client

This dependency-free sample intentionally demonstrates the initialize-capable compatibility era. New clients should use an official MCP SDK for `server/discover`, per-request metadata, and automatic fallback.

```python
import json
import subprocess
import sys

proc = subprocess.Popen(
    ["roslynmcp"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=sys.stderr,
    text=True,
    bufsize=1,
)

def send(msg: dict) -> None:
    proc.stdin.write(json.dumps(msg) + "\n")
    proc.stdin.flush()

def read_response(expected_id: int) -> dict:
    # Correlate by id so this remains correct when requests are concurrent.
    for line in proc.stdout:
        if not line.strip():
            continue
        msg = json.loads(line)
        if msg.get("id") == expected_id:
            return msg

# 1. initialize
send({
    "jsonrpc": "2.0", "id": 1, "method": "initialize",
    "params": {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "my-client", "version": "0.1"},
    },
})
print("init:", read_response(1))

# 2. initialized notification (no id)
send({"jsonrpc": "2.0", "method": "notifications/initialized"})

# 3. load a workspace
send({
    "jsonrpc": "2.0", "id": 2, "method": "tools/call",
    "params": {
        "name": "workspace_load",
        "arguments": {"path": r"C:\path\to\your.sln"},
    },
})
print("load:", read_response(2))
```

For first-run onboarding after `workspace_load`, call `workspace_readiness_report` with the returned `workspaceId`. It reports one compact verdict (`ready`, `restore-needed`, `build-needed`, or `analyzer-limited`), limitations, and next recommended workflows without running tests by default. Keep `workspace_support_bundle` for incident/recovery support on an already-running session.

## Minimal C# client

```csharp
using System.Diagnostics;
using System.Text.Json;

var psi = new ProcessStartInfo("roslynmcp")
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
};
using var proc = Process.Start(psi)!;

void Send(object msg)
{
    proc.StandardInput.WriteLine(JsonSerializer.Serialize(msg));
    proc.StandardInput.Flush();
}

JsonElement ReadResponse(int expectedId)
{
    while (proc.StandardOutput.ReadLine() is string line)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement.Clone();
        if (root.TryGetProperty("id", out var id) && id.GetInt32() == expectedId)
            return root;
    }
    throw new InvalidOperationException("Server closed stdout before response.");
}

// 1. initialize
Send(new { jsonrpc = "2.0", id = 1, method = "initialize",
    @params = new {
        protocolVersion = "2024-11-05",
        capabilities = new { },
        clientInfo = new { name = "my-client", version = "0.1" },
    } });
Console.WriteLine(ReadResponse(1));

// 2. initialized notification (no id)
Send(new { jsonrpc = "2.0", method = "notifications/initialized" });

// 3. workspace_load
Send(new { jsonrpc = "2.0", id = 2, method = "tools/call",
    @params = new {
        name = "workspace_load",
        arguments = new { path = @"C:\path\to\your.sln" },
    } });
Console.WriteLine(ReadResponse(2));
```

## Session lifetime

- Sessions live only for the stdio process lifetime — there's **no inactivity TTL**. A `KeyNotFoundException` on `workspaceId` means the host died (Cursor/Claude Code may transparently restart it between conversations), `workspace_close` was called, or the concurrent-workspace cap (`ROSLYNMCP_MAX_WORKSPACES`, default 8) evicted the session.
- Recovery: call `workspace_load` again with the same path. It's idempotent — repeat loads of the same path return the existing `workspaceId`.

## When things go wrong

- **"Server not initialized"**: you skipped the `notifications/initialized` step after the `initialize` response.
- **Parse errors on line 1**: you sent LSP `Content-Length:` headers. Drop them — NDJSON is one JSON object per line.
- **`workspaceId` NotFound**: the host restarted; reload with `workspace_load`.
- **No response at all**: check stderr, or the configured `file` sink. The server logs load errors and per-tool warnings there.

## Next steps

- Read `ai_docs/runtime.md` for environment variables, session lifetime, and the MCP runtime contract.
- Read `roslyn://server/catalog` (resource) or call `server_info` for the authoritative live surface inventory.
