# MCP token-overhead + SDK/spec conformance audit — 2026-08-13

<!-- Evidence report for the backlog rows tagged [source: 20260813 mcp-audit].
     Method: live stdio probe of the installed v2.3.8 binary (initialize + tools/list +
     prompts/list + resources/list), source-level surface inventory, SDK release-note
     research, MCP spec/best-practices research. Token figures = ceil(chars/4) on wire
     JSON; treat as ±15%. No build/test run (self-hosted runner constraint).
     CORRECTION: the installed v2.3.8 probe predated repo PR #1223, while the audited
     checkout already pinned ModelContextProtocol 2.1.0. SDK-current conclusions below
     distinguish the historical installed binary from checkout ground truth. -->

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

## 3. Gating: surface-wide tier enforcement

- `ROSLYNMCP_TOOL_TIERS` selects `stable` or `stable,experimental` (the default). Startup fails on empty, unknown, or experimental-only values.
- `SurfaceRegistrationPolicy` filters the SDK's authoritative tool, prompt, and resource collections after assembly discovery. Direct call/get/read routes for filtered members therefore fail as unavailable rather than bypassing list filtering.
- Server instructions and catalog/resource-template documents use the same selection, so stable-only discovery does not recommend experimental endpoints. Static lists are deterministic and carry five-minute private caching hints; resource bodies are private and immediately stale.
- `StartupDiagnostics` compares registered counts against the selected-tier expectation while separately proving reflection still matches the complete catalog.

## 4. SDK / spec currency

| | Pinned | Latest | |
|---|---|---|---|
| ModelContextProtocol (checkout) | 2.1.0 | 2.1.0 (2.0.0 → 2026-07-28; 2.1.0 → 2026-08-05) | current via PR #1223 |
| Microsoft.CodeAnalysis.* | 5.6.0 | 5.6.0 | current |
| MCP protocol | 2025-06-18 | **2026-07-28** (2025-11-25 intermediate) | two revisions stale |

SDK 2.x already provides the checkout with protocol 2026-07-28 negotiation + `server/discover` (automatic), tasks extension (`ModelContextProtocol.Extensions.Tasks`, SEP-2663), caching hints (`ttlMs`/`cacheScope`, SEP-2549), structured-content fidelity + JSON Schema 2020-12 (SEP-2106), MRTR elicitation, filter/request-handler expansion (MCPEXP002), and OTel trace-context (SEP-414). The v2.3.8 wire snapshot did not exercise that upgraded checkout.

Residual 2.x migration work is tracked separately: deprecated Logging/Roots/Sampling compatibility surfaces, structured raw-emission versus the bespoke `StructuredCallToolFilter` envelope, and public-contract migrations. The upgrade itself shipped in PR #1223 and compiles under the repository's warning policy.

## 5. Conformance scorecard (vs 2026-08 best practices)

Historical installed-binary result: 7 pass · 12 partial · 10 fail. Six failures were limitations of the probed pre-upgrade binary, not blockers in the checkout after PR #1223. This remediation closes the ServerInstructions, outputSchema projection, tool-tier registration, and static-list caching gaps; remaining checkout work includes output-size handling, input examples, and further feature adoption on the 2.x APIs.

Key partials/facts:
- **outputSchema (historical probe):** the installed v2.3.8 binary advertised zero schemas although 8 tools emitted runtime `structuredContent`. The checkout now projects `ToolOutputSchemaIndex` into `ProtocolTool.OutputSchema`; `ServerDiscoveryWireTests` guards the live initialize/tools-list path.
- **Logging**: declared capability + `notifications/message` bridge, but `McpLoggingProvider.cs:32` hardcodes an Information floor — `logging/setLevel` ignored. Do NOT patch: the whole surface is deprecated (2026-07-28); migrate per `mcp-logging-stderr-otel-migration` and let the bug die with it.
- Annotations 173/173 honest (four boolean hints; no per-tool `title`). Elicitation spec-shaped with strict allowlist. Progress + cancellation broadly implemented. Handle-based state already 2026-07-28-shaped.
- **Parameter object apply route (remediated):** the tool description and class documentation now route preview tokens through `apply_with_verify`; the nonexistent `apply_refactoring` name is absent from the shipped source.

## 6. Prior art (why no dynamic toolsets)

`tools/list_changed` honored reliably only by VS Code; Claude Desktop/Codex/Gemini CLI/Cursor drop it. GitHub MCP server removed its dynamic-toolsets mode 2026-05-20 (github/github-mcp-server PR #2512) — static selection + client deferral won. Static tier/toolset flags (GitHub `--toolsets`, Playwright `--caps`, Azure MCP modes) are the ecosystem-proven shape.
