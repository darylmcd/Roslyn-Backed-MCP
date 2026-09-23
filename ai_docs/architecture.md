# Architecture

<!-- purpose: Current system layers, dependencies, data flow, and known gaps. -->

## Overview

- **Runtime:** C# / .NET 10, Roslyn 5.x, stdio MCP host (`PackAsTool` global tool + Claude Code plugin)
- **Shape:** Layered — Host (transport + tools) → Roslyn (semantic services) → Core (DTOs/contracts)

## Entry points

| Process / host | Entry-point file | Starts |
|----------------|------------------|--------|
| stdio MCP server | `src/RoslynMcp.Host.Stdio/Program.cs` | DI composition root, MCP stdio transport, tool/resource/prompt registration |

## Code Map

| Domain / Feature | Source path(s) | Key types | Tests |
|------------------|----------------|-----------|-------|
| Host / Transport | `src/RoslynMcp.Host.Stdio/` | `Program`, `StructuredCallToolFilter`, `*Tools.cs` partials | `tests/RoslynMcp.Tests/` (host/filter tests) |
| Core Contracts | `src/RoslynMcp.Core/` | DTOs, preview store, gate contracts | `tests/RoslynMcp.Tests/` |
| Roslyn Services | `src/RoslynMcp.Roslyn/` | `WorkspaceManager`, analysis/refactor services | `tests/RoslynMcp.Tests/` |
| Plugin Skills (shipped) | `skills/` | `skills/*/SKILL.md` workflows | `eng/verify-skills-are-generic.ps1` (genericity guard), `tests/RoslynMcp.Tests/Skills/` |
| Release/CI Automation | `eng/` | `verify-registry-readiness.ps1`, `aggregate-promotion-scorecards.ps1`, `process-audit-reports.ps1`, `verify-*.ps1` | `tests/RoslynMcp.Tests/Skills/` |
| Roslyn analyzers | `analyzers/ServerSurfaceCatalogAnalyzer/` | `ServerSurfaceCatalogAnalyzer` (catalog vs `[McpServer*]` parity), `StdoutWriteAnalyzer` (RMCP010) | `tests/RoslynMcp.Tests/` |
| Repo agents | `agents/` | `audit-phase-runner.md` | `tests/RoslynMcp.Tests/Skills/AuditPhaseRunnerHandoffTests.cs` |
| Fixture solutions | `samples/` | `SampleSolution/`, `BuildFailureSolution/`, `GeneratedDocumentSolution/`, `SecurityTestProject/` | Loaded by `tests/RoslynMcp.Tests/` |
| Repo scripts | `scripts/` | `seed-issue-labels.ps1` | — |
| Plugin runtime | `hooks/`, `.claude-plugin/` | `hooks/hooks.json`, `plugin.json`, `marketplace.json`, `mcp.json` | See Claude Code Plugin Layer |
| Codex publication boundary | `.codex/` | `.codex/hooks.json`, `.codex/hooks/pre-publish-changelog.ps1` | — |
| Shard-discovery fixtures | `tests/RoslynMcp.ShardDiscoveryFixtures/` | `DiscoveryFixtures.cs` | Exercised by `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs` |

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
| `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json` | Shipped plugin and marketplace manifests |
| `.claude-plugin/mcp.json` | Shipped plugin MCP descriptor; launches the release-matched `Darylmcd.RoslynMcp` package for plugin installs |
| `skills/` | 32 SKILL.md skill definitions composing Roslyn MCP tools into guided workflows (shipped with the plugin). Repo-only maintainer skills live in `.claude/skills/` and are not shipped. |
| `hooks/` | `hooks.json` with safety hooks (preview-before-apply guard, post-refactoring compile-check reminder) |
| User/session MCP client config | External registration for the `roslynmcp` stdio host; not shipped from this repository |

The shipped `.claude-plugin/mcp.json` descriptor is distinct from external user/session-scoped registrations; the repository ships no root `.mcp.json`. The plugin layer is pure orchestration (no C# code): skills compose tools by MCP name into workflows, and hooks enforce preview-before-apply and compile-after-refactor.

## Known Gaps

- IDE and CA analyzers not loaded in MSBuildWorkspace — only SDK-implicit diagnostics active at runtime (AUDIT-21).
- **`Host.Stdio.Middleware` ↔ `Host.Stdio.Tools` namespace cycle (accepted).** Middleware types (e.g. `StructuredCallToolFilter`) read tool metadata declared in `Host.Stdio.Tools`, and tool types declare the attributes middleware dispatches on (found via `get_namespace_dependencies(circularOnly=true)`).
  - **Why accepted.** Both sides ship in one `Host.Stdio` assembly and the coupling is metadata-only (no behavioral dependency).
  - **Cost of fix.** A tool-dispatch envelope shim: one indirection per tool call, ~3 net new files.
  - **Trigger.** A middleware feature needing a new tool *category* (not just a new tool) forces the envelope refactor.

## Deep Material

Historical rationale and superseded investigations belong in `archive/`.
