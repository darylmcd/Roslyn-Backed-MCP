# logging-info-observations — Observability Info digest

**row:** `logging-info-observations` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs`

## Acceptance

- [ ] Each observation is either filed as its own row or explicitly dropped with a reason in this row before closing.

## Evidence

- Logging audit 20260930-1340 (dimension A3/A9/A10); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

Info-level observations from the audit: (1) ModelContextProtocol.* records in the JSONL stream carry correlationId null (the correlation scope is pushed by the tool filter, after the SDK logs request-received); (2) records carry no pid/version — attribution depends on the per-pid filename and the single Startup record; (3) the stderr stream uses the Simple console formatter (prose), acceptable because the file sink is the machine stream, but it is the DEFAULT stream today (see logging-default-sink-disabled-stderr-swallowed); (4) workspace paths and MSBuild diagnostic text appear in records (operator-local; note for any future remote export).

**Approach:** Triage pass; most likely fold (2) into the fields row as a static `pid`/`version` header on the startup record.
