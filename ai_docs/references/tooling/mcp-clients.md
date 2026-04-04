# MCP Client Tooling Reference

<!-- purpose: How MCP clients connect to this server and policy pointers. -->

## Server process shape

| Mode | Command | When to use |
|------|---------|-------------|
| **Global tool** | `roslynmcp` (stdio) | Day-to-day; lowest startup overhead after `dotnet tool install -g RoslynMcp`. |
| **Repo dev** | `dotnet run --project src/RoslynMcp.Host.Stdio` | Working on the server itself; same stdio protocol. |
| **Container** | `docker run` → `dotnet RoslynMcp.Host.Stdio.dll` | Isolated/untrusted workspaces — see `docs/setup.md`. |

- **Transport:** stdio only for this host. `stdout` is MCP protocol; logs go to `stderr` (see `ai_docs/runtime.md`).
- **Machine-readable surface:** `roslyn://server/catalog` (`server_catalog`) and `server_info` tool — use at session start to align with stable vs experimental tiers.

## Repo-local configuration (this repository)

| File | Role |
|------|------|
| [`.mcp.json`](../../../.mcp.json) (repo root) | Registers `roslyn` with `type: stdio`, `command: roslynmcp`. Cursor loads project MCP when present. |
| `.cursor/mcp.json` or user MCP settings | Optional mirror; same `command`/`args` model. |

**Do not commit** machine-specific absolute paths. Prefer `roslynmcp` on `PATH` or document `dotnet run` with a **local** path in your own config only.

## Cursor

- Prefer **`roslynmcp`** (global tool) over `dotnet run` for repeated launches.
- If the tool is not installed, use `dotnet run --project <path-to-RoslynMcp.Host.Stdio>` with a path **you** maintain locally.
- Follow **`ai_docs/runtime.md`** (*Roslyn MCP client policy*): use the server for C# **refactoring**, not only discovery; use preview/apply flows for mutations.

## VS Code and other MCP clients

- Any client that supports **stdio MCP** can run the same command line as above.
- Pass no extra arguments unless your client documents them; the server uses environment variables for tuning (see `ai_docs/runtime.md` — e.g. execution gate, timeouts where documented).

## Claude Code / Codex / other agents

- Configure the MCP server with the same **stdio** pattern: command `roslynmcp` or `dotnet` with args `run --project ...`.
- Point agents at **`ai_docs/README.md`** for bootstrap and **`ai_docs/runtime.md`** for policy.

## Operational checklist

| Check | Action |
|-------|--------|
| Server reachable | `server_info` returns version; `server_catalog` JSON parses. |
| Workspace | `workspace_load` with `workspaceId`; keep id for scoped calls. |
| Refactors | Preview then apply; do not hand-edit large multi-file changes when a tool exists. |
| Client timeouts | Some clients send **-32001** on long operations; server has its own timeouts — see tool descriptions and `runtime.md`. |

## Related

- `docs/setup.md` — install global tool, Docker, CI artifacts
- `ai_docs/runtime.md` — build/run, Roslyn MCP client policy, environment notes
