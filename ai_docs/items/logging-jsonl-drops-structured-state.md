# logging-jsonl-drops-structured-state — Serialize ILogger state as structured fields in the JSON-lines sink

**row:** `logging-jsonl-drops-structured-state` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredResultProjector.cs`
- `src/RoslynMcp.Host.Stdio/Diagnostics/ServerObservability.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] JSON-lines records carry a `fields` object holding every named template argument of the log call, and `eventName` from EventId.Name.
- [ ] V3 works as `jq -r 'select(.eventName=="ToolCompleted")|.fields.elapsedMs'` — a p95 recipe needs no regex over `message`.
- [ ] FileServerObservabilitySink records carry exceptionTypes and stackFrameCount as fields, not prose.
- [ ] Existing secret-sentinel assertions still pass (state args are the same values already rendered in `message`).
- [ ] The file-provider drift test pins the full key set (ts, level, category, eventId, eventName, message, correlationId, fields).

## Evidence

- Logging audit 20260930-1340 (dimension A1/A5); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

JsonLinesFileLoggerProvider.Write keeps only the formatted message (line 78-84); the ILogger state key/values are discarded, so tool name, outcome and elapsedMs exist only inside prose ("Tool symbol_search completed; outcome=success; elapsedMs=1389"). V3 (p95 timing) needed a regex over `message` instead of a jq field read. The stderr sink emits eventName + an exception object while the file sink emits neither (line SO:93-100 flattens it to prose), so the same UnexpectedFailure event has two schemas. Every one of the 197 direct log calls + 10 LoggerMessage definitions loses its named arguments.

Provider doc-comment says callers must put only operator-safe data in the message; the state args are exactly the values interpolated into the message, so serializing them adds no new exposure. Exception objects stay excluded.

**Approach:** In JsonLinesFileLogger.Write read `state as IReadOnlyList<KeyValuePair<string, object?>>`, drop the synthetic {OriginalFormat} entry, and serialize the rest under `fields`; add eventName. Fix the root (the provider discards state) rather than regex-extracting from the message. Additive schema change; note it in docs/stdio-client-integration.md.
