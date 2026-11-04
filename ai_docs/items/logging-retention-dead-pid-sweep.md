# logging-retention-dead-pid-sweep — Sweep dead-process JSON-lines files at startup

**row:** `logging-retention-dead-pid-sweep` · **pri:** `Medium` · **size:** `M` · **deps:** `logging-retention-destroys-repro-evidence`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs` (constructor, per-pid file naming)
- `src/RoslynMcp.Host.Stdio/Program.cs` (provider construction)
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Add `ROSLYNMCP_OBSERVABILITY_RETENTION_DAYS` (default 14) and `ROSLYNMCP_OBSERVABILITY_MAX_TOTAL_BYTES` (default 64 MiB), parsed and validated beside the segment options, documented in `ai_docs/references/environment-variables.md`, `docs/setup.md`, `docs/stdio-client-integration.md`.
- [ ] Startup sweep of `roslyn-mcp-*.jsonl` files of other processes older than the retention days, then oldest-first until the total fits the cap; never the own-pid files, never files of a live pid, never a file held open by another live host (an `IOException` on delete means held: skip).
- [ ] Tests: dead-pid files older than N days removed; total cap enforced; live-pid and own-pid files preserved.
- [ ] Reuse the segment-writer seam left by the retention row; do not duplicate rotation authority (coordinate with `logging-file-sink-sync-io-per-record`).

## Evidence

- Split from `logging-retention-destroys-repro-evidence` (acceptance bullet 4 and the live-pid half of bullet 5) so that row ships segment retention and the oversized-record policy first. Planned by cold-reviewed plan 2026-11-04; the knobs are not introduced earlier so no documented knob is a no-op.
