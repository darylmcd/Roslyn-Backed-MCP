# tool-tier-gated-registration — ROSLYNMCP_TOOL_TIERS registration gate

**row:** `tool-tier-gated-registration` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs:55-57` (blanket `WithToolsFromAssembly()` trio) and `:195-309` (env-binding block the new var joins)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (`TryGetTool` O(1) supportTier lookup — parity-tested)
- `ai_docs/runtime.md` env-var table + `README.md` (document the lean profile)
- `tests/RoslynMcp.Tests/` (default-surface snapshot unchanged; stable-only count; bad-value fail-fast)

## Acceptance

- [ ] Default (var unset or `stable,experimental`): registered surface byte-identical to today — zero behavior change for existing installs
- [ ] `ROSLYNMCP_TOOL_TIERS=stable`: exactly the 113 stable tools register (tools/list ≈ 36k of 55k est. tokens)
- [ ] Unknown tier value → fail-fast startup error naming the valid vocabulary
- [ ] runtime.md + README document the profile as the recommendation for non-deferring clients (Claude Desktop, Codex CLI, Gemini CLI, Cursor)

## Evidence

- supportTier exists on all 173 tools but is reporting-only — no enforcement consumer anywhere; experimental tier = 60 tools ≈ ~19k tokens for opt-in users — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §3

## Context

First enforcement consumer for supportTier. Distinct from `tool-surface-pagination-or-tool-sets` (bounded catalog resources WITHOUT hiding tools — complementary, do not merge). Decide in plan: whether prompts referencing experimental tools tier-gate too or degrade gracefully. Ecosystem prior art: GitHub `--toolsets`, Playwright `--caps`, Azure MCP modes (report §6).
