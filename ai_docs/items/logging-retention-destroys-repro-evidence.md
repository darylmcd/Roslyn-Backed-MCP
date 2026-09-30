# logging-retention-destroys-repro-evidence — Bound JSON-lines retention without deleting repro evidence

**row:** `logging-retention-destroys-repro-evidence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Segment cap and segment count are configurable (env) and documented; default keeps ≥3 segments.
- [ ] Startup prunes files of other processes older than N days (default 14) or beyond a total-size cap, never the live pid file or files held open by another live host.
- [ ] Test: writing 3× the cap keeps the first-written record reachable within the retained segments up to the documented bound; test: prune leaves a live-pid file alone.

## Evidence

- Logging audit 20260930-1340 (dimension A12); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

RotateIfNeeded does File.Delete(RotatedFilePath) then File.Move (lines 160-161): once a session writes >10 MiB the startup, load and earliest-repro records are destroyed mid-session (Durable dimension b). Conversely nothing ever removes roslyn-mcp-<pid>.jsonl of exited processes (V6: prior pid file survived restart, which is right, but no pruning exists anywhere — rg for Delete/Enumerate in src finds no log pruning), so the directory grows by up to 10 MiB per session forever.

Also coordinate with logging-file-sink-sync-io-per-record (same rotate code).

**Approach:** Numbered segments (.1 .. .N) with shift-rename instead of delete; startup sweep of stale per-pid files using last-write time. Keep per-pid naming (concurrent hosts must not share a file).
