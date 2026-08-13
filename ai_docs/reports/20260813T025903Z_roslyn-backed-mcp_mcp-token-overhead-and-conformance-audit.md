# MCP token-overhead + SDK/spec conformance audit — 2026-08-13

<!-- Evidence report for the backlog rows tagged [source: 20260813 mcp-audit].
     Method: live stdio probe of the installed v2.3.8 binary (initialize + tools/list +
     prompts/list + resources/list), source-level surface inventory, SDK release-note
     research, MCP spec/best-practices research. Token figures = ceil(chars/4) on wire
     JSON; treat as ±15%. No build/test run (self-hosted runner constraint). -->

## 1. Measured startup surface (wire probe, v2.3.8, protocol 2025-06-18)

| Component | Chars | Est. tokens |
|---|---|---|
| tools/list (173 tools, 1 page) | 220,068 | ~55,017 (avg 318/tool) |
| prompts/list (20) | 9,562 | ~2,391 |
| resources/list (5 fixed; 9 templates unprobed) | ~2,008 | ~502 |
| initialize `instructions` | **0** | 0 |
| **Total per non-deferring session** | ~231,600 | **~57,900** |

- Description text = 127,087 chars (57.8%): method-level 68,408 + schema-property 58,679. Annotations 15,488 chars.
- Claude Code default ToolSearch deferral: names-only at start ≈ **1.5–2k tokens** — already ~97% reduced. Server-side reduction serves non-deferring clients (Claude Desktop, Codex CLI, Gemini CLI, Cursor, proxies, pre-4.5 platforms).
- Top offenders (chars): workspace_load 3,746 · semantic_grep 3,348 (description 2,244 — **exceeds Claude Code's 2KB truncation**) · compile_check 3,136 · find_references 3,061.

## 2. Surface inventory (source, HEAD)

- 173 tools = 113 stable + 60 experimental; 49 preview-side + 26 apply-family; 17 strict preview/apply name pairs.
- All 49 previews `readOnly=true, destructive=false` (uniform class — preview merges lose no annotation granularity).
- Apply-family risk buckets: formatting 3 · text-edit 3 · code-transform 10 · file-lifecycle 5 · project-file 1 · composite/undo/fork 4+.
- 18 disjoint consolidation groups; max merge 173→~113; **risk-aligned merge (buckets preserved) 173→~117**, est. net ~10–13k tokens.
- Every apply takes only `previewToken` (+workspaceId) — structural proof for the apply merges. `symbol_refactor_preview` already ships the kind-discriminated pattern.

## 3. Gating: taxonomy exists, zero enforcement

- Registration is blanket `WithToolsFromAssembly()` (Program.cs:55-57); no env/CLI gate; `supportTier` consumed only by reporting (catalog summary, server_info, promotion diff) — never at registration/list time.
- Prompts/resources are on-demand; catalog resource is paginated + cap-safe.

## 4. SDK / spec currency

| | Pinned | Latest | |
|---|---|---|---|
| ModelContextProtocol | 1.4.1 | 2.1.0 (2.0.0 → 2026-07-28; 2.1.0 → 2026-08-05) | major behind |
| Microsoft.CodeAnalysis.* | 5.6.0 | 5.6.0 | current |
| MCP protocol | 2025-06-18 | **2026-07-28** (2025-11-25 intermediate) | two revisions stale |

SDK 2.x buys: protocol 2026-07-28 negotiation + `server/discover` (automatic), tasks extension (`ModelContextProtocol.Extensions.Tasks`, SEP-2663), caching hints (`ttlMs`/`cacheScope`, SEP-2549), structured-content fidelity + JSON Schema 2020-12 (SEP-2106), MRTR elicitation, filter/request-handler expansion (MCPEXP002), OTel trace-context (SEP-414).

Breaking risk 1.4.1→2.x for this repo: **MCP9005** (Logging deprecated — hits `McpLoggingProvider`; build break under warnings-as-errors; prerequisite = `mcp-logging-stderr-otel-migration` row) · structured raw-emission vs bespoke `StructuredCallToolFilter` envelope (Medium) · csharp-sdk#844 binding-exception contract re-verify (Medium) · error-code renumbering -32020..22 (Low-Med). Release-note-derived, not compile-verified.

## 5. Conformance scorecard (vs 2026-08 best practices)

7 pass · 12 partial · 10 fail. Fail cluster: 6 SDK-blocked (protocol rev, server/discover, tasks, caching hints, 2020-12 dialect) + 4 fixable on 1.4.1 (ServerInstructions; outputSchema wire projection; output-size handling; input_examples).

Key partials/facts:
- **outputSchema**: 8 adopter tools (server_info, server_heartbeat, workspace_* family) emit runtime `structuredContent`, but wire `toolsWithOutputSchema: 0` — schemas live only in the catalog resource; nothing projects `ToolOutputSchemaIndex` into `ProtocolTool.OutputSchema` (all tools return `Task<string>`). Projection gap, not adoption gap.
- **Logging**: declared capability + `notifications/message` bridge, but `McpLoggingProvider.cs:32` hardcodes an Information floor — `logging/setLevel` ignored. Do NOT patch: the whole surface is deprecated (2026-07-28); migrate per `mcp-logging-stderr-otel-migration` and let the bug die with it.
- Annotations 173/173 honest (four boolean hints; no per-tool `title`). Elicitation spec-shaped with strict allowlist. Progress + cancellation broadly implemented. Handle-based state already 2026-07-28-shaped.
- `ParameterObjectTools.cs:22` (+ class doc :13) routes agents to nonexistent `apply_refactoring` — shipped-surface defect.

## 6. Prior art (why no dynamic toolsets)

`tools/list_changed` honored reliably only by VS Code; Claude Desktop/Codex/Gemini CLI/Cursor drop it. GitHub MCP server removed its dynamic-toolsets mode 2026-05-20 (github/github-mcp-server PR #2512) — static selection + client deferral won. Static tier/toolset flags (GitHub `--toolsets`, Playwright `--caps`, Azure MCP modes) are the ecosystem-proven shape.
