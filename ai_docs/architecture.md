# Architecture

<!-- purpose: Current system layers, dependencies, data flow, and known gaps. -->

## Layer Map

| Layer | Project | Responsibility |
|-------|---------|----------------|
| Host | `src/RoslynMcp.Host.Stdio/` | MCP protocol transport, tool/resource/prompt wiring, DI startup |
| Core | `src/RoslynMcp.Core/` | DTOs, request/response contracts, shared abstractions, preview store |
| Roslyn | `src/RoslynMcp.Roslyn/` | MSBuildWorkspace, semantic navigation, analysis, refactoring |
| Tests | `tests/RoslynMcp.Tests/` | Integration and behavior validation against real workspaces |

## Dependency Graph

```
Host.Stdio
  ├── Core       (DTOs, contracts)
  └── Roslyn
        └── Core
```

No raw Roslyn types (`SyntaxNode`, `ISymbol`, etc.) cross the `Core` boundary.
No transport-specific code in `Roslyn` layer.

## Data Flow

```
MCP client → stdin
  → Host.Stdio (deserialize, dispatch)
    → Roslyn services (MSBuildWorkspace, semantic analysis)
      → Core DTOs (results)
    → Host.Stdio (serialize)
  → stdout → MCP client
```

Operational logs → `stderr` only (stdout reserved for MCP protocol traffic).

## Key Abstractions

| Abstraction | Location | Purpose |
|-------------|----------|---------|
| `workspaceId` | Core | Session-scoped handle; must be passed on all workspace operations |
| Preview/Apply flow | Roslyn + Core | Mutations are previewed before applying; workspace version guards apply step |
| `roslyn://server/catalog` | Host | Machine-readable surface inventory (tools, resources, prompts) |
| `server_info` tool | Host | Human-readable server surface summary |

## Surface Tiers

| Tier | Stability guarantee |
|------|-------------------|
| Stable | Compatibility and deprecation rules apply (see `docs/release-policy.md`) |
| Experimental | No compatibility guarantee; preview-first constraints required |
| Prompts | Not compatibility-stable |

## Key Boundaries

- Keep transport-specific concerns in host layer.
- Keep public service boundaries DTO-based (no raw Roslyn types crossing boundaries).
- Keep mutation flows preview-first and workspace-version aware.

## Safety Invariants

- Prefer stable tool/resource surface by default.
- Validate with build/tests before merge-ready handoff.
- Update docs/tests when behavior or surface contracts change.

## Claude Code Plugin Layer

The server is also distributed as a Claude Code plugin. Plugin artifacts live outside the C# project structure:

| Directory | Purpose |
|-----------|---------|
| `.claude-plugin/` | Plugin manifest (`plugin.json`) and marketplace descriptor (`marketplace.json`) |
| `skills/` | 10 SKILL.md skill definitions composing Roslyn MCP tools into guided workflows |
| `hooks/` | `hooks.json` with safety hooks (preview-before-apply guard, post-refactoring compile-check reminder) |
| `.mcp.json` | MCP server config with userConfig env var passthrough |

The plugin layer is a pure orchestration concern — it does not add code to the C# projects. Skills reference tools by their MCP names and compose them into multi-step workflows. Hooks enforce safety patterns (preview before apply, compile after refactor).

## Known Gaps

- IDE and CA analyzers not loaded in MSBuildWorkspace — only SDK-implicit diagnostics active at runtime (AUDIT-21).

## Deep Material

Historical rationale and superseded investigations belong in `archive/`.
