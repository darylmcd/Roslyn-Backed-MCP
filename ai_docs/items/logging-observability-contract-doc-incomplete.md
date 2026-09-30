# logging-observability-contract-doc-incomplete — Complete the observability contract docs with agent recipes

**row:** `logging-observability-contract-doc-incomplete` · **pri:** `Medium` · **size:** `S` · **deps:** `logging-jsonl-drops-structured-state,logging-event-id-collisions-no-catalog`

## Anchors

- `docs/stdio-client-integration.md`
- `AGENTS.md`

## Acceptance

- [ ] Docs table answers the A10 questions with exact rg/jq commands that were run in this audit (V1, V2, V3, V5).
- [ ] Event-code table present (generated from the catalog row).
- [ ] AGENTS.md carries the agent-first observability contract section pointing at the consumer doc.
- [ ] Doc link checker (verify-ai-docs) passes.

## Evidence

- Logging audit 20260930-1340 (dimension A10); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

Consumer docs (docs/stdio-client-integration.md:35-65) document the path, sink values, verbosity env keys and six record fields — solid — but omit the file name pattern (roslyn-mcp-<pid>.jsonl + .1 rotation), a correlate-one-request command, a timing (p95) recipe, the heartbeat invocation as a recipe, and the event-code table. AGENTS.md has no observability section at all, so contributor agents re-derive the surface every session. Published artifact: contract docs must ship consumer-visible.

Write after the fields/eventName/catalog rows so recipes use final names.

**Approach:** Fill the A10 template from the audit prompt with the verified commands; keep AGENTS.md section a pointer table, consumer doc the authority.
