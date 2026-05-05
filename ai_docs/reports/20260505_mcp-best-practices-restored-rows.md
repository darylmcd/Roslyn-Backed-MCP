# 2026-05-05 MCP best-practices backlog rows — provenance note

<!-- purpose: Provenance + recovery context for the 14 backlog rows added 2026-05-05 from the MCP-best-practices comparison and subsequently lost-and-restored. The next agent processing any of these rows should read this note first. -->
<!-- scope: in-repo -->

## What this note covers

Fourteen backlog rows whose `do` cells cite `Source: 2026-05-05 MCP-best-practices comparison (this session) §3 rec A–J` were drafted in a parallel session that compared this server against the MCP 2025-06-18 spec, the C# SDK's current capability surface, and the project's own retro evidence. The rows landed in `ai_docs/backlog.md` working-tree-only (never committed), then were lost when a downstream session ran `git reset --hard origin/main` while the dirty backlog file was unstaged. They were reconstructed verbatim from the diff captured in the same conversation's tool-output history and re-added by the restoration commit.

This note exists so the next agent picking up any of the recommendations has the original framing and can connect each backlog `id` back to the recommendation letter the row's `do` cell references.

## Recommendation map (`do` cell `§3 rec X` → backlog row id)

| Rec | Backlog row id(s) | One-line theme |
|-----|---|---|
| A | `tool-output-schema-infrastructure`, `tool-output-schema-batch-1-server-info-workspace` | Adopt MCP 2025-06-18 `outputSchema` + `structuredContent` — infrastructure first, then per-tool batches |
| B | `workspace-cache-store-infrastructure`, `workspace-load-uses-cache-fast-path` | Persistent cache for project graph + metadata-reference list to cut `workspace_load` cold-start (P95 ~45s on OrchardCore) |
| C | `progress-emit-audit-coverage` | Audit `IProgress` emission across the long-blocking tools (`workspace_load`, `workspace_warm`, `build_workspace`, `test_run`) and add stage-level emission where missing |
| D | `elicit-disambiguation-on-multi-symbol-resolve`, `elicit-workspace-path-on-missing-required-arg` | Use MCP `elicitation/create` (2025-06-18, C# SDK `McpServer.ElicitAsync`) for symbol disambiguation and missing-required-arg recovery; falls back gracefully when the client lacks the capability |
| E | `apply-with-verify-diff-not-counts` | Switch `apply_with_verify` from count-delta to identity-diff (id+file+line); ~14% false-positive rollback rate observed in retro |
| F | `workspace-warm-default-above-50-projects` | Default-on `workspace_warm` after `workspace_load` for >50-project solutions; may be obsoleted by `workspace-cache-prewarm-on-load` |
| G | `mcp-registry-publication` | Submit to the public MCP Registry (live since late 2025) for discoverability beyond the GitHub plugin marketplace |
| H | `http-streamable-host-project` (Defer) | Add `src/RoslynMcp.Host.Http/` for MCP Streamable HTTP transport; deferred pending a concrete remote-deployment driver |
| I | `sampling-driven-tool-flows-spike` | Half-day spike to identify which tools genuinely benefit from server-initiated `sampling/createMessage` over the agent's outer loop |
| J | `tool-surface-pagination-or-tool-sets` | Track surface-count growth (167 today; ~200 trigger threshold) and consider tool-set partitioning for small-model discovery |

## Tier 1 follow-on rows that don't carry a single-letter rec

- `workspace-cache-prewarm-on-load` — stitches the rec-B cache + rec-F warm strategies together (Medium; depends on `workspace-load-uses-cache-fast-path`).

## Cross-cutting evidence sources cited by these rows

- `ai_docs/reports/20260504T200153Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — 14-day, 40-session retro. Cited for: §2a row 4 (locator ambiguity), §2b row 1 (locator preflight), §3 #2 (`InvalidArgument` cluster), §3 #4 (apply-with-verify rollback rate), §3 #6 (cold-load timeout cluster), §4 #6 (warm strategy).
- `docs/large-solution-profiling-baseline.md` — OrchardCore (227 projects, captured 2026-04-26). Cited for: `workspace_load` P95 = 44.85s, `workspace_warm` P95 = 17.32s, `symbol_search` P95 = 1.18s, `find_references` P95 = 997ms.
- `docs/roadmap.md` § HTTP/SSE Hosting + § Claude Code Plugin Distribution. Cited for: `http-streamable-host-project` deferral rationale; `mcp-registry-publication` alignment.
- MCP spec 2025-06-18. Cited for: § Tools / Structured Content (rec A), § Elicitation (rec D), § Sampling (rec I), § Streamable HTTP transport (rec H).

## Recovery context (for the curious / for postmortem)

- The rows were drafted in working-tree-only state during a parallel session. They were never staged or committed.
- A downstream session running `/ship`-style cleanup ran `git reset --hard origin/main` against a dirty working tree, discarding the unstaged rows.
- The hook layer caught a prior `git stash` attempt as destructive but did not gate `git reset --hard` the same way. Lesson recorded under `~/.claude/projects/.../memory/` (orchestrator-side, not in this repo).
- Reconstruction was possible because the conversation's earlier `git diff ai_docs/backlog.md` tool-output captured the full pre-loss content of every added row. The restoration commit re-added them verbatim.
- If any row text reads slightly differently from how the original drafting session would have phrased it, prefer the on-disk row text (this restoration) over any older copy — the diff capture is the most-recent ground truth before loss.

## Suggested processing order for the next agent

The rows are sized for `/backlog-sweep:plan` per Rules 1/3/4/5. Run a normal sweep — this note is just orientation, not a sequencing override. The structural picks the next sweep likely makes:

1. Two High-band rows with no deps that block other work: `tool-output-schema-infrastructure` and `workspace-cache-store-infrastructure`. Their direct follow-ons (`tool-output-schema-batch-1-...` and `workspace-load-uses-cache-fast-path`) wait one sweep.
2. `progress-emit-audit-coverage` is independent and a good audit-investigation initiative to schedule in the same sweep if context allows.
3. Two `elicit-*` rows are paired — both implement the same MCP elicitation capability path. The planner may bundle them under Rule 1 if and only if they share a single `StructuredCallToolFilter` change set; otherwise ship as two initiatives.
4. `apply-with-verify-diff-not-counts` and `mcp-registry-publication` are independent Mediums good for parallel-mode batches.
5. The Defer row (`http-streamable-host-project`) and the two Low rows (`sampling-driven-tool-flows-spike`, `tool-surface-pagination-or-tool-sets`) stay parked until their respective gates trip (named users + auth plan; spike result; surface count near 200 OR external small-model discovery friction reported).
