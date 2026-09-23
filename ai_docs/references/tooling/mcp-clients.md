# MCP Client Tooling Reference

<!-- purpose: How MCP clients connect to this server and policy pointers. -->

## Server process shape

| Mode | Command | When to use |
|------|---------|-------------|
| **Claude Code plugin** | `dnx Darylmcd.RoslynMcp@<release> --source https://api.nuget.org/v3/index.json` (`.claude-plugin/mcp.json`) | Recommended for Claude Code; release-matched, no global shim required. |
| **Global tool** | `roslynmcp` (stdio) | Other clients; lowest startup overhead after `dotnet tool install -g Darylmcd.RoslynMcp`. |
| **Repo dev** | `dotnet run --project src/RoslynMcp.Host.Stdio` | Working on the server itself; same stdio protocol. |
| **Container** | `docker run` → `dotnet RoslynMcp.Host.Stdio.dll` | Isolated/untrusted workspaces — see `docs/setup.md`. |

- **Transport:** stdio only. `stdout` is MCP protocol; logs go to `stderr` (see `ai_docs/runtime.md`).
- **Machine-readable surface:** `roslyn://server/catalog` (`server_catalog`) and `server_info` — use at session start to align with stable vs experimental tiers.
- **Filesystem boundary (mandatory):** set `ROSLYNMCP_SANCTIONED_ROOTS`; with it empty, path access is denied (fail-closed). Setup and example: [docs/setup.md § Configure the filesystem boundary](../../../docs/setup.md#configure-the-filesystem-boundary). Variable table: [environment-variables.md](../environment-variables.md).

## Client configuration files (this repository)

| File | State | Role |
|------|-------|------|
| `.claude-plugin/mcp.json` | tracked | Plugin launch through `dnx`, with `ROSLYNMCP_SANCTIONED_ROOTS`. |
| `.cursor/mcp.json`, `.vscode/mcp.json` | tracked | Register `roslyn` as stdio `command: roslynmcp` (no `env` block). |
| `.mcp.json` (repo root) | gitignored, not shipped | Optional local file; examples in [README Per-Client Config](../../../README.md#per-client-config) and `docs/mcp-json-examples/`. |

Do not commit machine-specific absolute paths.

## Per-client notes

- **Cursor / VS Code / other stdio clients:** run `roslynmcp` (or `dotnet run --project <local path>`); pass no extra arguments, tune with environment variables.
- **Claude Code:** install the plugin (commands in [README](../../../README.md)); it bundles skills and safety hooks and launches the server through `dnx`. Local plugin dev: `claude --plugin-dir <path-to-checkout>`.
- **Codex and other agents:** configure stdio the same way.
- **All clients:** follow `ai_docs/runtime.md` (*Roslyn MCP client policy*); bootstrap starts at `ai_docs/README.md`.

## Operational checklist

| Check | Action |
|-------|--------|
| Server reachable | `server_info` returns version; `server_catalog` JSON parses. |
| Workspace | `workspace_load` takes `path` (absolute `.sln`/`.slnx`/`.csproj`) and **returns** the `workspaceId`; keep it for scoped calls. |
| Refactors | Preview then apply; do not hand-edit large multi-file changes when a tool exists. |
| Client timeouts | Some clients send **-32001** on long operations; server has its own timeouts — see tool descriptions and `runtime.md`. |

## Related

- `docs/setup.md` — install, Docker, CI artifacts
- `ai_docs/runtime.md` — build/run, Roslyn MCP client policy
