# Roslyn-Backed MCP Server

[![CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml)

A production-usable MCP (Model Context Protocol) server that provides semantic C# analysis capabilities powered by Roslyn, without requiring Visual Studio. Designed for AI coding agents to semantically navigate, analyze, and refactor real C# solutions.

## AI Session Fast Start

For the shortest safe path in new agent sessions:

1. Read `AGENTS.md` first.
2. Read `CI_POLICY.md` for validation and merge-gating expectations.
3. Read `ai_docs/README.md`, then `ai_docs/workflow.md` and `ai_docs/runtime.md`.
4. Use `server_info` and `roslyn://server/catalog` to verify runtime surface.

### 30-Second AI Quick Path

1. Load workspace and keep `workspaceId`.
2. Follow `ai_docs/workflow.md` for branch/worktree/PR behavior.
3. Follow `CI_POLICY.md` for validation and merge handoff.
4. Use stable tools/resources first and preview/apply for mutations.

## Quick Start

For packaging, Docker, the global `dotnet` tool, and CI artifact names, see [docs/setup.md](docs/setup.md).

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — **10.0.100** per [`global.json`](global.json) (`rollForward` is `latestFeature`; compatible **10.0.x** patches generally work). See [docs/setup.md](docs/setup.md).

### Build

```bash
dotnet build RoslynMcp.slnx
```

### Run

```bash
dotnet run --project src/RoslynMcp.Host.Stdio
```

### Test

```bash
dotnet test RoslynMcp.slnx
```

## Cross-Platform Notes

The server runs on Windows, macOS, and Linux wherever the .NET 10 SDK is available. Known platform-specific behavior:

- **Path separators**: The server normalizes paths internally but MCP clients should send OS-native paths (backslashes on Windows, forward slashes elsewhere).
- **MSBuild locator**: Uses `Microsoft.Build.Locator` to find the SDK. On Linux/macOS, ensure the SDK is on `PATH` or set `DOTNET_ROOT`.
- **Symlink resolution**: `ClientRootPathValidator` resolves symlinks and junctions before path comparison. Behavior varies by filesystem — NTFS junctions on Windows, symlinks on Unix.
- **Process management**: `dotnet build` and `dotnet test` child processes use `Process.Kill(entireProcessTree: true)` on cancellation, which requires `SIGKILL` support on Unix (available on all modern distributions).
- **File watchers**: `FileSystemWatcher` reliability varies by OS and filesystem. NFS and some container-mounted volumes may not emit change events.

## Security Considerations

**This server executes MSBuild evaluation when loading `.sln` and `.csproj` files.** MSBuild project files can contain arbitrary build targets, tasks, and imports that run native code during evaluation. This means:

- **Only load solutions you trust.** A malicious `.csproj` can execute arbitrary code with the permissions of the server process.
- **Run in a sandbox for untrusted code.** If you need to analyze untrusted repositories, run the server inside a container, VM, or other isolation boundary.
- **Path validation is enforced** against MCP client roots when available, including symlink/junction resolution, but this is a defense-in-depth measure — not a substitute for trusting the workspace content.

See [SECURITY.md](SECURITY.md) for the vulnerability disclosure policy.

## Claude Code Plugin Installation

The easiest way to use this server with Claude Code is as a plugin.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — **10.0.100** per [`global.json`](global.json)
- Install the global tool: `dotnet tool install -g Darylmcd.RoslynMcp` (the unprefixed `RoslynMcp` package id is owned by another publisher; the CLI command remains `roslynmcp` after install)

### Install as Plugin

```bash
# Add the marketplace
/plugin marketplace add darylmcd/Roslyn-Backed-MCP

# Install the plugin
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

Or for local development/testing:

```bash
# Load directly from a local checkout
claude --plugin-dir /path/to/Roslyn-Backed-MCP
```

### Available Skills

| Skill | Description |
|-------|-------------|
| `/roslyn-mcp:analyze` | Solution health check — diagnostics, complexity, cohesion, vulnerabilities |
| `/roslyn-mcp:refactor` | Guided semantic refactoring — rename, extract, move, split |
| `/roslyn-mcp:review` | Semantic code review — diagnostics, dead code, complexity, security |
| `/roslyn-mcp:document` | XML documentation generator for public APIs |
| `/roslyn-mcp:security` | Security audit — OWASP diagnostics, CVE scan, reflection, DI |
| `/roslyn-mcp:dead-code` | Dead code detection and safe cleanup |
| `/roslyn-mcp:test-coverage` | Test coverage analysis and test scaffolding |
| `/roslyn-mcp:migrate-package` | NuGet package migration across projects |
| `/roslyn-mcp:explain-error` | Diagnostic explanation with auto-fix |
| `/roslyn-mcp:complexity` | Complexity hotspot and god class analysis |

### Plugin Hooks

The plugin includes safety hooks that enforce the preview/apply pattern:

- **Pre-apply guard**: Blocks `*_apply` calls that weren't preceded by a `*_preview`.
- **Post-apply verifier**: Reminds the agent to run `compile_check` after structural refactorings.

## MCP Client Configuration

### Cursor

Add to `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\src\\RoslynMcp.Host.Stdio"]
    }
  }
}
```

### Claude Code

Add to Claude Code MCP settings:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\src\\RoslynMcp.Host.Stdio"]
    }
  }
}
```

### Install as Global Tool (Recommended)

Pack and install as a dotnet global tool for the best startup performance and MCP client configuration:

```bash
dotnet publish src/RoslynMcp.Host.Stdio -c Release /p:ReinstallTool=true
```

This single command builds, packs the NuGet package, kills any running `roslynmcp` processes, and installs (or updates) the global tool. After installation, configure your MCP client to use the tool command:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "roslynmcp"
    }
  }
}
```

To update after code changes, run the same publish command again. The `/p:ReinstallTool=true` flag handles the full cycle automatically.

### Published Executable (Alternative)

If you prefer a standalone publish without global tool installation:

```bash
dotnet publish src/RoslynMcp.Host.Stdio -c Release -o ./publish
```

Then point your MCP client at the published executable:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "C:\\path\\to\\publish\\RoslynMcp.Host.Stdio.exe"
    }
  }
}
```

For a reproducible release build and publish verification:

```powershell
./eng/verify-release.ps1
```

## Configuration

The server reads optional environment variables at startup. When set, these override the built-in defaults. See [`ai_docs/runtime.md`](ai_docs/runtime.md) for the full list of advanced overrides (path validation, scripting concurrency, abandoned-script caps, etc.).

| Environment Variable | Default | Description |
|---|---|---|
| `ROSLYNMCP_MAX_WORKSPACES` | `8` | Maximum concurrent workspace sessions |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `300` | Build operation timeout (seconds) |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `600` | Test run timeout (seconds) |
| `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` | `120` | NuGet vulnerability scan timeout (seconds) |
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `20` | Preview store entries per store |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `5` | Preview store entry lifetime (minutes) |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `120` | Maximum requests per rate-limit window |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `60` | Rate-limit window (seconds) |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `120` | Per-request timeout (seconds) |
| `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` | `10` | `evaluate_csharp` script budget (seconds) |

Example:

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

## Privacy Policy

This MCP server runs entirely on your local machine and does not collect, transmit, or store any telemetry, analytics, or personal data.

- **Data processed**: The server reads `.sln`, `.csproj`, and `.cs` files from workspaces you explicitly load. All analysis happens in-process using Roslyn.
- **Network access**: The server makes no outbound network requests. `dotnet build` and `dotnet test` child processes may download NuGet packages as part of normal .NET SDK behavior.
- **Data retention**: Workspace state exists only in memory for the duration of the server process. Preview stores expire entries after 5 minutes. No data is persisted to disk beyond what `dotnet build`/`dotnet test` produce.
- **Logging**: Diagnostic logs are emitted via MCP's logging notification channel to the connected client. No logs are written to disk or sent to external services.
- **Third-party sharing**: No data is shared with Anthropic, any third party, or any external service.

For privacy questions, open an issue at [github.com/darylmcd/Roslyn-Backed-MCP/issues](https://github.com/darylmcd/Roslyn-Backed-MCP/issues).

## Support

- **Bug reports and feature requests**: [GitHub Issues](https://github.com/darylmcd/Roslyn-Backed-MCP/issues)
- **Contributing**: See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.
- **Security vulnerabilities**: See [SECURITY.md](SECURITY.md) for the disclosure policy.

## Canonical Docs

- `AGENTS.md` is the canonical bootstrap entry point for AI agents.
- `CLAUDE.md` is the Claude bootstrap mirror.
- `.github/copilot-instructions.md` is a thin bootstrap file for Copilot.
- `CI_POLICY.md` is the canonical validation and merge-gating policy.
- `ai_docs/README.md` is the canonical AI-doc routing index.
- `ai_docs/workflow.md` is the canonical git/branch/worktree/PR workflow policy.
- `ai_docs/runtime.md` is the canonical runtime and execution-context reference.
- `ai_docs/backlog.md` is the canonical unfinished-work list.
- `.cursor/rules/operational-essentials.md` is a compact reminder layer aligned with `ai_docs/workflow.md`.
- `roslyn://server/catalog` is the canonical machine-readable surface contract exposed by the running server.

## Project Map

- `src/RoslynMcp.Host.Stdio/`: host startup, MCP wrappers, tool/resource/prompt registration, logging.
- `src/RoslynMcp.Core/`: shared contracts, DTOs, interfaces, and preview-store abstractions.
- `src/RoslynMcp.Roslyn/`: Roslyn workspace, semantic analysis, diagnostics, refactoring, and execution services.
- `tests/RoslynMcp.Tests/`: integration and behavior coverage across stable and experimental surfaces.
- `samples/`: fixture solutions used by tests for realistic workflows.
- `eng/verify-release.ps1`: release verification path for publish and hashes.
- `.claude-plugin/`: Claude Code plugin manifest and marketplace descriptor.
- `skills/`: 10 Claude Code skill definitions composing MCP tools into guided workflows.
- `hooks/`: plugin safety hooks enforcing preview/apply and compile-check patterns.

## Supported Surface

Catalog **`2026.04`** ships **123 tools** (66 stable / 57 experimental), **9 resources** (all stable), and **16 prompts** (all experimental). Use the `server_info` tool and the `roslyn://server/catalog` resource for the authoritative live surface — the categories below are a quick orientation.

### Stable tool families

- workspace session management and inspection
- source text and source-generated document reads
- semantic symbol navigation, relationships, references, completions, type-usage and type-mutation tooling
- diagnostics, `compile_check`, impact analysis, `list_analyzers`, and related semantic analysis tools (including `analyze_snippet`)
- security tools (`security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`)
- read-only advanced-analysis tools (`find_unused_symbols`, `get_complexity_metrics`, `get_di_registrations`, `get_namespace_dependencies`, `get_nuget_dependencies`, `find_reflection_usages`)
- build/test discovery, execution, related-test lookup, and coverage
- preview/apply refactoring workflows for rename, organize-usings, format document, and curated code fixes
- `server_info`

### Experimental tool families

- `semantic_search`, flow analysis (`analyze_data_flow`, `analyze_control_flow`), `get_operations`, `get_syntax_tree`
- direct text-edit tools (`apply_text_edit`, `apply_multi_file_edit`, `add_pragma_suppression`)
- workspace file operations (create / move / delete preview + apply)
- project file mutations (package, project, framework, conditional property, central package management) and MSBuild evaluators
- scaffolding (type, test) preview + apply
- dead-code removal preview + apply
- generic Roslyn code actions (`get_code_actions`, `preview_code_action`, `apply_code_action`)
- experimental refactoring (extract interface/type, move type to file, bulk replace type, fix-all, format range)
- cross-project refactoring and orchestration (move type to project, dependency inversion, package migration, split class, extract+wire interface, composite apply)
- editor configuration (`get_editorconfig_options`, `set_editorconfig_option`, `set_diagnostic_severity`)
- scripting (`evaluate_csharp`)
- undo (`revert_last_apply`)

### Stable resources

- `roslyn://server/catalog`, `roslyn://server/resource-templates`
- `roslyn://workspaces` and `roslyn://workspaces/verbose`
- `roslyn://workspace/{workspaceId}/status` and `.../status/verbose`
- `roslyn://workspace/{workspaceId}/projects`
- `roslyn://workspace/{workspaceId}/diagnostics`
- `roslyn://workspace/{workspaceId}/file/{filePath}`

The `verbose` siblings opt into the full per-project tree; the default summary keeps a status check at ~500 bytes instead of ~30 KB on large solutions.

### Experimental prompts (16)

`explain_error`, `suggest_refactoring`, `review_file`, `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `discover_capabilities`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`.

### Product Boundaries

- The current production target is the local stdio host on a developer workstation.
- Workspace state comes from `MSBuildWorkspace` and on-disk files, not unsaved editor buffers.
- HTTP/SSE hosting is intentionally deferred to a future host project.
- Live IDE parity requires a separate editor-backed integration path and is not implied by the current host.

## Architecture

```
.claude-plugin/              Claude Code plugin manifest + marketplace
skills/                      10 plugin skills (orchestration prompts)
hooks/                       Plugin safety hooks (preview/apply guard)
src/
  RoslynMcp.Host.Stdio/      MCP stdio host (thin tool wrappers)
  RoslynMcp.Core/            DTOs, service interfaces, PreviewStore
  RoslynMcp.Roslyn/          Roslyn workspace/symbol/refactoring services
tests/
  RoslynMcp.Tests/           Unit + integration tests
samples/
  SampleSolution/            Multi-project test solution
```

## Agent Workflow (End-To-End)

1. Load workspace and persist `workspaceId`.
2. Run stable-first semantic navigation and diagnostics.
3. Produce preview tokens for mutation/refactoring operations.
4. Apply preview only if workspace version has not changed.
5. Run build/test validation loops.
6. Re-run diagnostics and update docs/tests for any surface change.

### Key Design Decisions

- **Transport-agnostic core**: Only `Host.Stdio` references the MCP SDK. An HTTP/SSE host can be added later by referencing the same Core and Roslyn libraries.
- **DTOs at the boundary**: Roslyn types (`ISymbol`, `Document`, `Compilation`) never cross the service boundary. All public APIs return serializable DTOs.
- **Session-aware workspaces**: `workspace_load` returns a dedicated `workspaceId`. Every semantic and refactoring tool is scoped to an explicit session instead of relying on a singleton loaded solution.
- **Preview/apply with version gating**: Refactoring operations use a two-step preview/apply pattern. Preview tokens are tied to a specific workspace session and version and are rejected if that workspace changes between preview and apply.
- **Flexible symbol targeting**: Tools can resolve symbols from file path + 1-based line/column, stable symbol handles emitted in symbol DTOs, or fully qualified metadata names where appropriate.
- **Validation loop built in**: Agents can stay inside the MCP surface for build/test discovery and execution instead of shelling out ad hoc for every edit cycle.
- **Curated fixes over generic mutation**: Diagnostic fixes are intentionally opt-in and preview-first. The server exposes a small, deterministic curated set instead of arbitrary code actions.
- **stderr-only logging**: stdout is reserved exclusively for MCP protocol messages.
- **MSBuildWorkspace**: Uses real MSBuild project loading for accurate analysis of `.sln`/`.csproj` files, not ad-hoc workspace hacks.

## Deferred Features

| Feature | Reason |
|---------|--------|
| `extract_method_preview` | Roslyn's `ExtractMethodCodeRefactoringProvider` requires internal IDE workspace services absent from `MSBuildWorkspace`. Verified in v1.9.0. Other selection-range refactorings (introduce parameter, inline temporary variable) **do** work via `get_code_actions` with `endLine`/`endColumn`. |
| HTTP/SSE transport | v1 focuses on local stdio. The architecture supports adding an ASP.NET Core host project later. |
| Generic file read/write tools | Editors (Cursor, VS Code) already provide these. |
| Memory/knowledge-base features | Out of scope for a semantic analysis server. |
| AI summarization | Out of scope. |
| Arbitrary code generation | Out of scope for v1. |

## Requirements

- .NET 10 SDK (`10.0.100`+ per [`global.json`](global.json); `rollForward` is `latestFeature`)
- No Visual Studio installation required (MSBuild is included in the SDK)
- Windows is the primary v1 target; macOS and Linux are supported wherever the .NET 10 SDK runs (see *Cross-Platform Notes* above for known platform-specific behaviour)

## Validation

The integration suite covers:

- workspace load/reload/status with explicit `workspaceId` sessions
- workspace-scoped build/test discovery and execution with passing and failing fixtures
- symbol search, symbol info, definition lookup, references, implementations, type hierarchy, callers/callees, and impact analysis
- override/base-member lookup, member hierarchy, project graph, signature help, relationship summaries, and generated-document listing
- separated workspace/load, compiler, and analyzer diagnostics
- preview/apply behavior for rename, organize-usings, formatting, and curated code fixes on isolated sample-solution copies
- stale preview rejection when a workspace session changes after preview creation
- wrapper/integration coverage for server catalog, resources, prompts, syntax, completions, direct edits, multi-file edits, code-action contracts, and coverage response shape
- hardening coverage for workspace-load validation, workspace-count limits, related-test scan bounds, and command timeout enforcement
