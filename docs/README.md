# Docs

Human-facing documentation for the Roslyn-Backed MCP Server.

## Contents

| File | Purpose |
|------|---------|
| `setup.md` | Prerequisites, build/test/run, global tool and Docker, CI artifacts |
| `reinstall.md` | Step-by-step Layer 1 (global tool) + Layer 2 (Claude Code plugin) reinstall workflow after a code change |
| `stdio-client-integration.md` | NDJSON framing, handshake order, and minimal Python/C# examples for custom MCP clients |
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
