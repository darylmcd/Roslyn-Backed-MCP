# Roslyn-Backed MCP Server

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

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.104+)

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

### Published Executable

For better startup performance, publish first:

```bash
dotnet publish src/RoslynMcp.Host.Stdio -c Release -o ./publish
```

Then use the published executable:

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

## Supported Surface

The server exposes a larger surface than earlier versions of the README documented. Support is now split into stable and experimental tiers.

### Stable Tool Families

- workspace session management and inspection
- source text and generated-document reads
- semantic symbol navigation, relationships, references, completions, and type-usage tooling
- diagnostics, impact analysis, and related semantic analysis tools
- build/test discovery, execution, and related-test lookup
- preview/apply refactoring workflows
- `server_info`

### Experimental Tool Families

- advanced-analysis tools
- direct text-edit tools
- workspace file operation tools
- project mutation tools
- scaffolding tools
- dead-code removal tools
- syntax-tree inspection
- generic Roslyn code actions
- coverage collection

### Stable Resources

- `server_catalog`
- `workspaces`
- `workspace_status`
- `workspace_projects`
- `workspace_diagnostics`
- `source_file`

### Experimental Prompts

- `explain_error`
- `suggest_refactoring`
- `review_file`
- `analyze_dependencies`
- `debug_test_failure`

### Product Boundaries

- The current production target is the local stdio host on a developer workstation.
- Workspace state comes from `MSBuildWorkspace` and on-disk files, not unsaved editor buffers.
- HTTP/SSE hosting is intentionally deferred to a future host project.
- Live IDE parity requires a separate editor-backed integration path and is not implied by the current host.

## Architecture

```
src/
  RoslynMcp.Host.Stdio/    MCP stdio host (thin tool wrappers)
  RoslynMcp.Core/          DTOs, service interfaces, PreviewStore
  RoslynMcp.Roslyn/        Roslyn workspace/symbol/refactoring services
tests/
  RoslynMcp.Tests/         Unit + integration tests
samples/
  SampleSolution/                  Multi-project test solution
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
| `extract_method_preview` | Roslyn's extract-method support requires internal IDE service layers (`CodeRefactoringProvider`) that are not stable public APIs. Deferred until a clean public-API path exists. |
| HTTP/SSE transport | v1 focuses on local stdio. The architecture supports adding an ASP.NET Core host project later. |
| Generic file read/write tools | Editors (Cursor, VS Code) already provide these. |
| Memory/knowledge-base features | Out of scope for a semantic analysis server. |
| AI summarization | Out of scope. |
| Arbitrary code generation | Out of scope for v1. |

## Requirements

- .NET 10 SDK
- No Visual Studio installation required (MSBuild is included in the SDK)
- Windows (primary target for v1)

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
