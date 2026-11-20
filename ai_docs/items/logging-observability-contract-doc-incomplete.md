# logging-observability-contract-doc-incomplete — Complete the observability contract docs with agent recipes

**row:** `logging-observability-contract-doc-incomplete` · **pri:** `Medium` · **size:** `S` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `docs/stdio-client-integration.md`
- `AGENTS.md`

## Acceptance

- [x] File-name pattern (`roslyn-mcp-{pid}.jsonl` plus `.N` rotation) documented in `docs/stdio-client-integration.md` (Operator observability, retention paragraph). Shipped with #1962.
- [x] Timing recipe (`jq` over `ToolCompleted`) and the dependency-boundary `jq` recipe present in the same doc.
- [x] Event-code table present, drift-tested against `LogEvents` (#2060).
- [ ] Add an explicit correlate-one-request recipe: one `jq` command that selects every record for a given `correlationId` and orders by `ts`. The doc only says the id joins records in prose.
- [ ] Add a health recipe that shows the exact `server_heartbeat` invocation and the fields an agent reads from it (today only one prose sentence names the tool).
- [ ] `AGENTS.md` carries a short agent-first observability contract section (pointer table: sink env var, file path, event catalog, recipes) that points at the consumer doc; it has no observability section today.
- [ ] Doc link checker (`verify-ai-docs`) passes.

## Evidence

- Logging audit 20260930-1340 (dimension A10); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

Consumer docs (docs/stdio-client-integration.md:35-65) document the path, sink values, verbosity env keys and six record fields — solid — but omit the file name pattern (roslyn-mcp-<pid>.jsonl + .1 rotation), a correlate-one-request command, a timing (p95) recipe, the heartbeat invocation as a recipe, and the event-code table. AGENTS.md has no observability section at all, so contributor agents re-derive the surface every session. Published artifact: contract docs must ship consumer-visible.

Write after the fields/eventName/catalog rows so recipes use final names.

**Approach:** Fill the A10 template from the audit prompt with the verified commands; keep AGENTS.md section a pointer table, consumer doc the authority.

## Notes

2026-11-20: re-derived at 847d7f94: file-name pattern, timing recipe and event table already shipped (#1962, #2060); remaining Acceptance narrowed to correlate recipe, heartbeat recipe, AGENTS.md section.
