# MCP token-overhead + SDK/spec conformance audit — 2026-08-13

<!-- Evidence report for the backlog rows tagged [source: 20260813 mcp-audit].
     Method: live stdio probe of the installed v2.3.8 binary (initialize + tools/list +
     prompts/list + resources/list), source-level surface inventory, SDK release-note
     research, MCP spec/best-practices research. Token figures = ceil(chars/4) on wire
     JSON; treat as ±15%. No build/test run (self-hosted runner constraint).
     CORRECTION: the installed v2.3.8 probe predated repo PR #1223, while the audited
     checkout already pinned ModelContextProtocol 2.1.0. SDK-current conclusions below
     distinguish the historical installed binary from checkout ground truth. The probe's
     initialize capability payload was not retained, so no capability-dependent conclusion
     from that run is portable to the current checkout. -->

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
- Server instructions and catalog/resource-template documents use the same selection, so stable-only discovery does not recommend experimental endpoints. Static lists are deterministic. On 2026-07-28 requests they carry five-minute private cache hints and resource bodies carry zero-TTL private hints; legacy responses omit those modern-only fields.
- `StartupDiagnostics` compares registered counts against the selected-tier expectation while separately proving reflection still matches the complete catalog.

## 4. SDK / spec currency

| Component | 2026-08-13 observation | Correct current interpretation |
|---|---|---|
| ModelContextProtocol checkout | 2.1.0 | Directly adopted from 1.4.1 in PR #1223; intervening package releases are evaluated in ADR 0003. |
| Microsoft.CodeAnalysis checkout | 5.6.0 | Source-inventory fact; unrelated to the historical wire binary. |
| MCP wire | Installed RoslynMcp 2.3.8 requested 2025-06-18; client capabilities were not retained. | The SDK 2.1 checkout supports initialization-based legacy sessions through 2025-11-25 and modern 2026-07-28 discovery/request metadata. The old probe cannot establish either checkout path. |

SDK 2.x already provides the checkout with protocol 2026-07-28 negotiation + `server/discover` (automatic), tasks extension (`ModelContextProtocol.Extensions.Tasks`, SEP-2663), caching hints (`ttlMs`/`cacheScope`, SEP-2549), structured-content fidelity + JSON Schema 2020-12 (SEP-2106), MRTR elicitation, filter/request-handler expansion (MCPEXP002), and OTel trace-context (SEP-414). The v2.3.8 wire snapshot did not exercise that upgraded checkout.

At report time, the residual inventory included deprecated Logging/Roots/Sampling compatibility surfaces, structured raw emission versus the bespoke filter envelope, and public-contract migrations. Current delivered state is recorded by ADR 0003, release notes, and durable regressions; the live backlog is a temporary planning view, and this historical report does not override those sources.

Durable SDK-lineage, protocol-era, and consumer-migration decisions live in
[`docs/decisions/0003-sdk-2x-wire-compatibility.md`](../../docs/decisions/0003-sdk-2x-wire-compatibility.md).
This report's installed-binary probe must not be cited as raw-wire evidence for those checkout decisions.

## 5. Conformance scorecard (vs 2026-08 best practices)

Historical installed-binary result: 7 pass · 12 partial · 10 fail. That score is retained only as an artifact of the installed 2.3.8 probe; it is not a checkout conformance score. The earlier claim that source inspection closed output-schema and static-list caching gaps is retracted. Current claims require the dual-era raw-wire matrices named below; remaining work is tracked in the backlog and ADR 0003.

Key partials/facts:
- **outputSchema (historical probe):** the installed v2.3.8 binary advertised zero schemas although 8 tools emitted runtime `structuredContent`. The checkout now projects `ToolOutputSchemaIndex` into `ProtocolTool.OutputSchema`; `ServerDiscoveryWireTests` guards discovery, while `StructuredContentWireContractTests` is the by-eight/by-era payload and advertised-schema evidence.
- **Caching (corrected checkout evidence):** a new legacy raw probe found the repository filters still emitted modern cache fields despite SDK 2.1's own protocol gate. `ServerDiscoveryWireTests` now owns the legacy-omission/modern-value matrix.
- **Logging**: declared capability + `notifications/message` bridge, but `McpLoggingProvider.cs:32` hardcodes an Information floor — `logging/setLevel` ignored. Do NOT patch: the whole surface is deprecated (2026-07-28); migrate per `mcp-logging-stderr-otel-migration` and let the bug die with it.
- Annotations 173/173 were a source-inventory observation (four boolean hints; no per-tool `title`). The old probe did not prove modern elicitation or cancellation behavior; MRTR migration and cancellation propagation remain separately tracked.
- **Parameter object apply route (remediated):** the tool description and class documentation now route preview tokens through `apply_with_verify`; the nonexistent `apply_refactoring` name is absent from the shipped source.

## 6. Prior art (why no dynamic toolsets)

`tools/list_changed` honored reliably only by VS Code; Claude Desktop/Codex/Gemini CLI/Cursor drop it. GitHub MCP server removed its dynamic-toolsets mode 2026-05-20 (github/github-mcp-server PR #2512) — static selection + client deferral won. Static tier/toolset flags (GitHub `--toolsets`, Playwright `--caps`, Azure MCP modes) are the ecosystem-proven shape.
