# Direct stdio client integration

This doc exists for orchestrators that spawn `roslynmcp` as a child process and speak MCP directly, rather than through a pre-configured Claude Code / Cursor integration.

If you're using Claude Code (via the `/plugin install roslyn-mcp@roslyn-mcp-marketplace` flow) or Cursor's MCP config UI, you don't need this doc — the client handles the protocol for you. Read on only if you're writing your own harness.

## Protocol at a glance

- **Transport:** NDJSON (newline-delimited JSON) on stdin/stdout. Each message is a single JSON object followed by `\n`. **Do not** use LSP-style `Content-Length:` framing — the server ignores headers and treats the first non-JSON byte as a parse error.
- **Stderr** is for operational logging. Do not parse it as protocol traffic.
- **Spec:** [Model Context Protocol](https://modelcontextprotocol.io). The server implements the stdio transport; the public MCP SDKs speak it natively.

## Init handshake — required order

1. **Spawn** `roslynmcp` (installed globally via `dotnet tool install -g Darylmcd.RoslynMcp`, CLI command is `roslynmcp`).
2. Send **`initialize`** request — capability negotiation. Response arrives on stdout with a matching `id`.
3. Send **`notifications/initialized`** — a notification (no `id`) telling the server the handshake is done. **Until this arrives, every `tools/call` will be rejected** with "Server not initialized."
4. Send any tool calls. The server accepts them after step 3.

### Stdout ordering caveat

Startup-time notifications (`notifications/message` log lines, `notifications/resources/list_changed`) may interleave with the `initialize` response on stdout. Filter responses by the `id` you sent; don't assume the first line after `initialize` is your response.

## Parameter naming

Every tool-call parameter uses the exact names shown in `tools/list`. A few that tend to trip up custom clients:

- **`workspace_load`** takes `path` (not `solutionPath` / `sln` / `project`).
- Every workspace-scoped tool takes `workspaceId` — the value returned by the `workspace_load` response. Reuse it for the entire session.
- JSON keys are camelCase. Response keys match (e.g. `workspaceId`, `previewToken`, not `WorkspaceId`).

## Minimal Python client

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
    # Drain notifications (no id) until we see the matching response.
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
        "protocolVersion": "2024-11-05",
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
- **No response at all**: check stderr. The server logs load errors and per-tool warnings there.

## Next steps

- Read `ai_docs/runtime.md` for environment variables, session lifetime, and the MCP runtime contract.
- Read `roslyn://server/catalog` (resource) or call `server_info` for the authoritative live surface inventory.
