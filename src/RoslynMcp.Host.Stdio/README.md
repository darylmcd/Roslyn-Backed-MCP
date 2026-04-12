# Darylmcd.RoslynMcp

A production-usable **Model Context Protocol (MCP) server** providing semantic C# analysis powered by Roslyn — designed for AI coding agents to navigate, analyze, and refactor real C# solutions without requiring Visual Studio.

This package installs `roslynmcp`, a global .NET tool that runs the MCP server over stdio.

> Source code, contributing guide, deep-dive design notes, and the full audit-driven backlog live on GitHub: **<https://github.com/darylmcd/Roslyn-Backed-MCP>**

## Install

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet tool install -g Darylmcd.RoslynMcp
```

This puts `roslynmcp` on your `PATH`. The CLI command is intentionally `roslynmcp` (not `darylmcd-roslyn-mcp`) so existing MCP client config keeps working — only the package id is author-prefixed because the unprefixed `RoslynMcp` id is owned by another publisher on nuget.org.

To update later:

```bash
dotnet tool update -g Darylmcd.RoslynMcp
```

## Use it with an MCP client

The server speaks MCP over **stdio**. Point any stdio-capable MCP client at the `roslynmcp` command.

### Cursor

Add to `.cursor/mcp.json` (project) or your user MCP settings:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "roslynmcp"
    }
  }
}
```

### Claude Code (manual MCP config)

Add to your Claude Code MCP settings:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "roslynmcp"
    }
  }
}
```

### Claude Code (plugin — recommended)

Claude Code also supports installing this server as a **plugin** with curated skills and safety hooks. The plugin is a thin layer on top of the same `roslynmcp` global tool — install the global tool first, then add the plugin:

```
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

The plugin adds 10 guided slash commands (`/roslyn-mcp:analyze`, `/roslyn-mcp:refactor`, `/roslyn-mcp:review`, `/roslyn-mcp:security`, `/roslyn-mcp:dead-code`, `/roslyn-mcp:test-coverage`, `/roslyn-mcp:migrate-package`, `/roslyn-mcp:explain-error`, `/roslyn-mcp:complexity`, `/roslyn-mcp:document`) and pre-apply safety hooks that block `*_apply` calls without a matching `*_preview`. See the [GitHub README](https://github.com/darylmcd/Roslyn-Backed-MCP#claude-code-plugin-installation) for the full skill documentation.

### VS Code (and other stdio MCP clients)

Any stdio-capable MCP client uses the same command:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "roslynmcp"
    }
  }
}
```

## What's in the box

Catalog `2026.04` ships **126 tools** (66 stable / 60 experimental), **9 resources**, and **16 prompts**. Use `server_info` and `roslyn://server/catalog` for the authoritative live surface; the categories below are a quick orientation.

| Family | Highlights |
|---|---|
| **Workspace** | `workspace_load`, `workspace_status`, `workspace_list`, `project_graph`, source-generated documents — workspace tools default to a lean **summary** payload (~500 bytes) and offer `verbose=true` opt-in for the full per-project tree. |
| **Symbol navigation** | `find_references`, `find_implementations`, `find_overrides`, `goto_definition`, `symbol_search`, `find_consumers`, `find_type_usages`, `type_hierarchy`, `callers_callees`, `impact_analysis`. |
| **Diagnostics** | `project_diagnostics`, `compile_check` (in-memory, with optional emit validation), `diagnostic_details`, `security_diagnostics`, `nuget_vulnerability_scan`, `list_analyzers`. |
| **Refactoring (preview / apply)** | `rename_*`, `extract_interface_*`, `extract_type_*`, `move_type_to_file_*`, `bulk_replace_type_*`, `code_fix_*`, `fix_all_*`, `format_document_*`, `organize_usings_*`, `split_class_*`, dead-code removal. |
| **Build / test** | `build_workspace`, `build_project`, `test_discover`, `test_run`, `test_related`, `test_related_files`, `test_coverage`. |
| **Cohesion / complexity** | `get_cohesion_metrics` (LCOM4, source-gen aware), `get_complexity_metrics`, `find_unused_symbols`, `find_type_mutations` (with `MutationScope` for IO/Network/Process/Database side effects). |
| **Flow analysis** | `analyze_data_flow`, `analyze_control_flow` — supports both statement-bodied and `=> expr` expression-bodied members. |
| **Snippets / scripting** | `analyze_snippet`, `evaluate_csharp` (with configurable script timeout). |
| **Resources** | `roslyn://server/catalog`, `roslyn://workspaces`, `roslyn://workspace/{id}/status` (lean summary defaults; `/verbose` siblings for full payloads), `roslyn://workspace/{id}/projects`, `roslyn://workspace/{id}/diagnostics`, `roslyn://workspace/{id}/file/{filePath}`. |

Every reader and writer response carries `_meta.elapsedMs`, `_meta.queuedMs`, and `_meta.heldMs` so concurrency audits can compute speedup ratios from inside the agent loop without external instrumentation.

## Configuration

Optional environment variables (override built-in defaults):

| Variable | Default | Purpose |
|---|---|---|
| `ROSLYNMCP_MAX_WORKSPACES` | `8` | Maximum concurrent workspace sessions |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `300` | Build operation timeout |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `600` | Test run timeout |
| `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` | `120` | NuGet vulnerability scan timeout |
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `20` | Preview store entries per store |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `5` | Preview store entry lifetime |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `120` | Per-window request count |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `60` | Rate limit window |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `120` | Per-request timeout |
| `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` | `10` | `evaluate_csharp` budget |

Example MCP client config with overrides:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "roslynmcp",
      "env": {
        "ROSLYNMCP_MAX_WORKSPACES": "4",
        "ROSLYNMCP_BUILD_TIMEOUT_SECONDS": "120"
      }
    }
  }
}
```

## Security

**This server executes MSBuild evaluation when loading `.sln` and `.csproj` files.** MSBuild project files can contain arbitrary build targets, tasks, and imports that run native code during evaluation:

- **Only load solutions you trust.** A malicious `.csproj` can execute arbitrary code with the permissions of the server process.
- **Run in a sandbox for untrusted code.** If you need to analyze untrusted repositories, run the server inside a container, VM, or other isolation boundary.
- Path validation is enforced against MCP client roots when available, including symlink/junction resolution — but this is defense-in-depth, not a substitute for trusting the workspace content.

See [SECURITY.md](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/SECURITY.md) for the vulnerability disclosure policy.

## Privacy

This server runs entirely on your local machine and does not collect, transmit, or store any telemetry, analytics, or personal data.

- **Data processed:** `.sln`, `.csproj`, and `.cs` files from workspaces you explicitly load. All analysis happens in-process via Roslyn.
- **Network access:** None from the server itself. `dotnet build` / `dotnet test` child processes may download NuGet packages as part of normal SDK behavior.
- **Data retention:** Workspace state is in-memory only; preview tokens expire after 5 minutes. No data is persisted to disk beyond what `dotnet build`/`test` produce.
- **Logging:** Diagnostic logs are emitted via MCP's logging notification channel to the connected client. No logs are written to disk or sent to external services.
- **Third-party sharing:** No data is shared with Anthropic, any third party, or any external service.

## Cross-platform notes

Runs on Windows, macOS, and Linux wherever the .NET 10 SDK is available.

- **MSBuild locator:** Uses `Microsoft.Build.Locator` to find the SDK. On Linux/macOS, ensure `dotnet` is on `PATH` or set `DOTNET_ROOT`.
- **File watchers:** `FileSystemWatcher` reliability varies by OS and filesystem. NFS and some container-mounted volumes may not emit change events.
- **Process management:** `dotnet build` and `dotnet test` child processes use `Process.Kill(entireProcessTree: true)` on cancellation, which requires `SIGKILL` support on Unix (available on all modern distributions).
- **Symlinks:** Path validation resolves symlinks and junctions before comparison; behavior varies between NTFS junctions on Windows and POSIX symlinks on Unix.

## Links

- **Source code, contributing, design notes:** <https://github.com/darylmcd/Roslyn-Backed-MCP>
- **Bug reports & feature requests:** <https://github.com/darylmcd/Roslyn-Backed-MCP/issues>
- **Changelog:** <https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/CHANGELOG.md>
- **Security disclosure:** <https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/SECURITY.md>

## License

MIT — see [LICENSE](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/LICENSE).
