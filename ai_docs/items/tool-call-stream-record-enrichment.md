# tool-call-stream-record-enrichment — Stamp correlation, elapsed ms, and timestamps onto stream records

**row:** `tool-call-stream-record-enrichment` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:306`
- `src/RoslynMcp.Host.Stdio/Program.cs:43`
- `tests/RoslynMcp.Host.Stdio.Tests/StructuredCallToolFilterTests.cs`

## Acceptance

- [ ] Every record emitted during a tool call carries the request correlation id via a logging scope (`BeginScope` in `StructuredCallToolFilter` + `IncludeScopes` on the formatter), not per-template interpolation.
- [ ] Tool completion AND failure templates include ElapsedMs (already measured by the filter's stopwatch) and outcome.
- [ ] Console records carry UTC ISO-8601 timestamps with millisecond precision.

## Evidence

- Live audit: correlation id appears on exactly one template (failure line) — `rg <correlationId>` over the captured stream returns 1 record, no chain; zero timestamps in stream — see `ai_docs/audits/20260825-1440/report.md` (A3/A5/A8) + `run1-stderr.log`.

## Notes

- Interacts with `correlation-id-suffix-single-source`: scope-stamping reduces the hand-interpolated `correlationId=` suffix pattern that row consolidates — coordinate, don't duplicate.
- Seed EventIds for the filter's records while touching the templates (A2, Info-scoped).
