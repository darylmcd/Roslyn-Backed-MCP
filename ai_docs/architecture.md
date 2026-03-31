# Architecture

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

## Known Gaps

- `TargetFrameworks` resolution shows "unknown" for most projects — MSBuildWorkspace TFM resolution not fully wired (DATA-03).
- `get_code_actions` returns empty at most positions — additional code fix providers needed (DATA-06).
- IDE and CA analyzers not loaded in MSBuildWorkspace — only SDK-implicit diagnostics active at runtime (AUDIT-21).

## Deep Material

Historical rationale and superseded investigations belong in `archive/`.
