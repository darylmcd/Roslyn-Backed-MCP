# Docs

Human-facing documentation for the Roslyn-Backed MCP Server.

## Contents

| File | Purpose |
|------|---------|
| [setup.md](setup.md) | Prerequisites, build/test/run, global tool and Docker, CI artifacts |
| [reinstall.md](reinstall.md) | Plugin refresh workflow plus the separate optional global-tool reinstall path |
| [stdio-client-integration.md](stdio-client-integration.md) | NDJSON framing, handshake order, and minimal Python/C# examples for custom MCP clients |
| [compatibility.md](compatibility.md) | MCP client compatibility matrix — tested/likely clients, install-path × client table, known gotchas |
| [product-contract.md](product-contract.md) | Session operating contract, stable vs experimental surface tiers, supported tool families |
| [release-policy.md](release-policy.md) | Release gates, compatibility rules, deprecation policy, versioning |
| [upgrade-matrix.md](upgrade-matrix.md) | SDK, Roslyn, MSBuild, analyzers, and related dependency coupling; what to bump together |
| [roadmap.md](roadmap.md) | Strategic roadmap decisions and planned feature directions |
| [parity-gap-matrix.md](parity-gap-matrix.md) | Hard boundaries vs roadmap opportunities; what agents should treat as known gaps |
| [parity-gap-implementation-plan.md](parity-gap-implementation-plan.md) | Status and next steps for matrix "must-have" items; release verify vs roadmap |
| [coverage-baseline.md](coverage-baseline.md) | Aggregate coverage expectations; ties to CI Cobertura artifacts |
| [experimental-promotion-analysis.md](experimental-promotion-analysis.md) | Promotion history and criteria for experimental -> stable changes |
| [large-solution-profiling-baseline.md](large-solution-profiling-baseline.md) | Methodology and notes for profiling large MSBuild solutions |
| [self-hosted-runner.md](self-hosted-runner.md) | Self-hosted GitHub Actions runner topology (PR validation stays on GitHub-hosted runners) |
| [mcp-json-examples/README.md](mcp-json-examples/README.md) | Copy-ready `.mcp.json` examples: [minimal](mcp-json-examples/minimal.mcp.json), [with-overrides](mcp-json-examples/with-overrides.mcp.json), [dnx](mcp-json-examples/dnx.mcp.json) |
| [decisions/README.md](decisions/README.md) | ADRs - decision log for breaking/compat/security decisions |

## Claude Code Plugin

The server ships as a Claude Code plugin. Plugin-specific documentation:

- [README.md](../README.md) § *Option C — Claude Code Plugin* — install commands and high-level usage
- [setup.md](setup.md) § *Claude Code Plugin* — packaging, local dev, and validation commands
- [product-contract.md](product-contract.md) § *Claude Code Plugin Surface* — how bundled skills relate to the MCP tool tiers

Plugin source files: `.claude-plugin/`, `skills/`, `hooks/`, `.claude-plugin/mcp.json`

## Related

- For AI-agent session bootstrap: `AGENTS.md`
- For in-repo planning and open work: `ai_docs/planning_index.md`, `ai_docs/backlog.md`
- For runtime/build commands and MCP client policy: `ai_docs/runtime.md`
