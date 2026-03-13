# Roslyn-Backed MCP Server

A production-usable MCP (Model Context Protocol) server that provides semantic C# analysis capabilities powered by Roslyn, without requiring Visual Studio. Designed for AI coding agents to semantically navigate, analyze, and refactor real C# solutions.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.104+)

### Build

```bash
dotnet build RoslynMcp.slnx
```

### Run

```bash
dotnet run --project src/Company.RoslynMcp.Host.Stdio
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
      "args": ["run", "--project", "C:\\path\\to\\src\\Company.RoslynMcp.Host.Stdio"]
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
      "args": ["run", "--project", "C:\\path\\to\\src\\Company.RoslynMcp.Host.Stdio"]
    }
  }
}
```

### Published Executable

For better startup performance, publish first:

```bash
dotnet publish src/Company.RoslynMcp.Host.Stdio -c Release -o ./publish
```

Then use the published executable:

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "C:\\path\\to\\publish\\Company.RoslynMcp.Host.Stdio.exe"
    }
  }
}
```

## Tool Surface (v1)

### Workspace Tools

| Tool | Description |
|------|-------------|
| `workspace_load` | Load a `.sln`, `.slnx`, or `.csproj` file and return a `workspaceId` session handle |
| `workspace_reload` | Reload a specific workspace session by `workspaceId` |
| `workspace_status` | Get loaded path, project/document counts, snapshot metadata, and workspace/load diagnostics for a `workspaceId` |

### Semantic Read Tools

| Tool | Description |
|------|-------------|
| `symbol_search` | Search symbols by name with optional kind/project/namespace filters |
| `symbol_info` | Get detailed info for a symbol by file:line:column or fully qualified metadata name |
| `go_to_definition` | Find definition locations by source position or stable symbol handle |
| `find_references` | Find all references across the solution by source position or stable symbol handle |
| `find_implementations` | Find implementations of interfaces/abstract members by source position or stable symbol handle |
| `document_symbols` | Get hierarchical symbol tree for a file in a specific workspace session |
| `project_diagnostics` | Get workspace/load, compiler, and analyzer diagnostics with severity filtering |
| `type_hierarchy` | Get base types, derived types, and interfaces by source position or stable symbol handle |
| `callers_callees` | Find direct callers and callees of a method by source position or stable symbol handle |
| `impact_analysis` | Analyze impact of changing a symbol by source position or stable symbol handle |

### Preview-First Refactoring Tools

| Tool | Description |
|------|-------------|
| `rename_preview` | Preview a rename by source position or stable symbol handle with unified diffs |
| `rename_apply` | Apply a previewed rename (rejects stale tokens) |
| `organize_usings_preview` | Preview removing/sorting usings |
| `organize_usings_apply` | Apply organize usings |
| `format_document_preview` | Preview formatting changes |
| `format_document_apply` | Apply formatting |

## Architecture

```
src/
  Company.RoslynMcp.Host.Stdio/    MCP stdio host (thin tool wrappers)
  Company.RoslynMcp.Core/          DTOs, service interfaces, PreviewStore
  Company.RoslynMcp.Roslyn/        Roslyn workspace/symbol/refactoring services
tests/
  Company.RoslynMcp.Tests/         Unit + integration tests
samples/
  SampleSolution/                  Multi-project test solution
```

### Key Design Decisions

- **Transport-agnostic core**: Only `Host.Stdio` references the MCP SDK. An HTTP/SSE host can be added later by referencing the same Core and Roslyn libraries.
- **DTOs at the boundary**: Roslyn types (`ISymbol`, `Document`, `Compilation`) never cross the service boundary. All public APIs return serializable DTOs.
- **Session-aware workspaces**: `workspace_load` returns a dedicated `workspaceId`. Every semantic and refactoring tool is scoped to an explicit session instead of relying on a singleton loaded solution.
- **Preview/apply with version gating**: Refactoring operations use a two-step preview/apply pattern. Preview tokens are tied to a specific workspace session and version and are rejected if that workspace changes between preview and apply.
- **Flexible symbol targeting**: Tools can resolve symbols from file path + 1-based line/column, stable symbol handles emitted in symbol DTOs, or fully qualified metadata names where appropriate.
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
- symbol search, symbol info, definition lookup, references, implementations, type hierarchy, callers/callees, and impact analysis
- separated workspace/load, compiler, and analyzer diagnostics
- preview/apply behavior for rename, organize-usings, and formatting on isolated sample-solution copies
- stale preview rejection when a workspace session changes after preview creation
