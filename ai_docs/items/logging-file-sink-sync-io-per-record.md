# logging-file-sink-sync-io-per-record — Keep the JSON-lines file open instead of open/stat/append per record

**row:** `logging-file-sink-sync-io-per-record` · **pri:** `Low` · **size:** `S` · **deps:** `logging-retention-destroys-repro-evidence`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesFileLoggerProvider.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] One FileStream (FileShare.Read, flush per record) and a tracked length replace per-record open/stat.
- [ ] Rotation and Dispose close/reopen the stream; test covers rotate-under-open-handle on Windows.
- [ ] Benchmark or timing test shows per-record cost drops (recorded in the PR).

## Evidence

- Logging audit 20260930-1340 (dimension A5); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

Write() (lines 95-101) performs four filesystem operations per record inside `lock (_writeLock)`, synchronously on whichever thread logs. At Debug verbosity the SDK alone emits dozens of records per request (V4: 23 Debug records for a 4-call session), serializing all request threads behind disk I/O. Not a correctness bug; it taxes exactly the verbose repro mode the sink exists to support.

Same lines as the retention row — sequenced after it.

**Approach:** Hold the stream inside the existing lock; flush per record so last-gasp records survive a crash. Avoid a background writer (would lose the final records on abrupt exit).

## Current cold review 2026-10-10

| Item | Evidence / obligation |
|---|---|
| Source | At927e969, provider95 creates the directory,97 appends synchronously, and76 locks the request thread; rotation probes existence/length. Mechanism unchanged from reviewed1cd4a90. |
| Ownership | Existing row/dependency owns the finding; no duplicate row or benchmark claim. Structured-state PR1777 does not fix this debt. |
| Complete repair | One owned writer/rotation authority; deterministic disposal/rotation handle release and representative enabled-file throughput proof. |
