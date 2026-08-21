# Docs

Human-facing documentation for the Roslyn-Backed MCP Server.

## Contents

| File | Purpose |
|------|---------|
| `setup.md` | Prerequisites, build/test/run, global tool and Docker, CI artifacts |
| `reinstall.md` | Plugin refresh workflow plus the separate optional global-tool reinstall path |
| `stdio-client-integration.md` | NDJSON framing, handshake order, and minimal Python/C# examples for custom MCP clients |
| `compatibility.md` | MCP client compatibility matrix — tested/likely clients, install-path × client table, known gotchas |
| `product-contract.md` | Session operating contract, stable vs experimental surface tiers, supported tool families |
| `release-policy.md` | Release gates, compatibility rules, deprecation policy, versioning |
| `upgrade-matrix.md` | SDK, Roslyn, MSBuild, analyzers, and related dependency coupling; what to bump together |
| `roadmap.md` | Strategic roadmap decisions and planned feature directions |
| `parity-gap-matrix.md` | Hard boundaries vs roadmap opportunities; what agents should treat as known gaps |
| `parity-gap-implementation-plan.md` | Status and next steps for matrix "must-have" items; release verify vs roadmap |
| `coverage-baseline.md` | Aggregate coverage expectations; ties to CI Cobertura artifacts |
| `experimental-promotion-analysis.md` | Promotion history and criteria for experimental -> stable changes |
| `large-solution-profiling-baseline.md` | Methodology and notes for profiling large MSBuild solutions |
| `mcp-json-examples/README.md` | Copy-ready `.mcp.json` examples and when to use them; indexes `mcp-json-examples/minimal.mcp.json` and `mcp-json-examples/with-overrides.mcp.json` |
| `decisions/0001-locationdto-nested-field-migration.md` | ADR: additive nested `Location` field on `SymbolDto`/`DiagnosticDto`/`TypeUsageDto`, legacy-flat-field deprecation window, and the bounded producer/consumer migration stages |
| `decisions/0002-configured-sanctioned-root-boundary.md` | ADR: server-owned sanctioned-root security boundary, canonical path resolution, and migration from client Roots authority |
| `decisions/0003-sdk-2x-wire-compatibility.md` | ADR: SDK 1.4.1→2.1.0 lineage, dual-protocol wire contract, correction classification, and consumer migration |

## Claude Code Plugin

The server ships as a Claude Code plugin. Plugin-specific documentation:

- `README.md` § *Claude Code Plugin Installation* — install commands and high-level usage
- `setup.md` § *Claude Code Plugin* — packaging, local dev, and validation commands
- `product-contract.md` § *Claude Code Plugin Surface* — how bundled skills relate to the MCP tool tiers

Plugin source files: `.claude-plugin/`, `skills/`, `hooks/`, `.mcp.json`

## Related

- For AI-agent session bootstrap: `AGENTS.md`
- For in-repo planning and open work: `ai_docs/planning_index.md`, `ai_docs/backlog.md`
- For runtime/build commands and MCP client policy: `ai_docs/runtime.md`
