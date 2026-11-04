# logging-retention-destroys-repro-evidence — Bound JSON-lines retention without deleting repro evidence

**row:** `logging-retention-destroys-repro-evidence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Segment threshold (`ROSLYNMCP_OBSERVABILITY_MAX_SEGMENT_BYTES`, with a floor) and segment count (`ROSLYNMCP_OBSERVABILITY_MAX_SEGMENTS`) are configurable and documented; default keeps >=3 numbered segments; rotation shifts segments and deletes only the oldest.
- [ ] Define and enforce a valid-JSON oversized-record policy (bounded marker record, truncating unbounded fields), preserving explicit evidence of the policy and documenting the resulting bound `max(segment, marker)`.
- [ ] Cover first, empty and nonempty files with one record exceeding the threshold; change `FileProvider_WritesScopedJsonLinesAndRotatesWithoutExceptionDetail`, which pins the oversized append; never imply the rotation threshold is a strict record or file ceiling.
- [ ] Writing 3x the threshold keeps the first-written record reachable within retained segments up to the documented bound.
- [ ] Startup pruning of other processes' files (age, total size, live-pid preservation) is carried by `logging-retention-dead-pid-sweep`; leave one segment-writer seam for it and for `logging-file-sink-sync-io-per-record`.

## Evidence

- Historical logging audit20260930-1340, dimension A12, at123ffd3a: `ai_docs/audits/20260930-1340/report.md`.
- Current session2026-10-10, immutable source a4a826dcf589068b908234759a1103cafe39bb8b: `RotateIfNeeded:147-164` returns for absent/empty files or rotates once before the whole pending record is appended. The threshold does not bound one oversized record; the historical10MiB/process ceiling claim was unsupported.
- Logging candidate PR1777 atcdb7c51f3e59d275ab541ddd48c3d21308f843fe retains this independent mechanism atprovider:200-216. Unchanged test `FileProvider_WritesScopedJsonLinesAndRotatesWithoutExceptionDetail` sets threshold450 and writes a500-character payload. Root re-read the actual code/test this session. Source-only retention finding; a dedicated oversized-record runtime reproduction remains required.

## Context

| Defect | Required complete repair |
|---|---|
| Prior segment deleted on rotation | Numbered retained segments; preserve early repro evidence within the configured bound. |
| Whole oversized record appended after one rotation | Explicit valid-JSON policy and first/empty/nonempty regression matrix; define the actual bound. |
| Dead-pid files never pruned | Owned startup sweep respecting live/held files, age and total-size configuration. |
| Concurrent hosts | Keep per-pid naming; hosts must not share active files. |

Coordinate with `logging-file-sink-sync-io-per-record`; do not silently duplicate rotation authority. Logging structured-state delivery does not claim to fix retention.
