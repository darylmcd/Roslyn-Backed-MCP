# observability-consumer-doc-contract — Ship a consumer-facing observability contract

**row:** `observability-consumer-doc-contract` · **pri:** `Medium` · **size:** `S` · **deps:** `observability-file-sink-full-stream,tool-call-stream-record-enrichment`

## Anchors

- `README.md:237`
- `ai_docs/runtime.md:110`

## Acceptance

- [ ] A consumer-visible doc section (README or docs/stdio-client-integration.md) answers: where logs land per host, verbosity env vars (`Logging__LogLevel__*` — currently documented NOWHERE despite working, live-verified), sink env var(s), correlation story, `server_info`/`server_heartbeat` health route.
- [ ] `ai_docs/runtime.md` cross-links the same contract without divergence.

## Evidence

- Live audit: `Logging__LogLevel__Default=Debug` flipped 68→202 stream lines with no rebuild yet appears in no doc — see `ai_docs/audits/20260825-1440/report.md` (A10/A7).

## Notes

- Depends on the sink + enrichment rows so the contract documents the final state once.
- Marketplace/registry installers read README/docs, never `ai_docs/` — that is the surface that counts (published artifact).
