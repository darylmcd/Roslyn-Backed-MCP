# Logging & Diagnostics Surface Audit — Roslyn-Backed-MCP

Run `20260930-1340` · HEAD `123ffd3a` · mode `full` · backlog `apply` · prompt: `~/.claude/prompts/logging-observability-audit-prompt.md`

## Repository summary

| Item | Value |
|---|---|
| App class | MCP server (stdio) — stdout = protocol; one extra private worker mode (`--script-worker`, parent-owned pipe) |
| Stack | C# / .NET 10, `Microsoft.Extensions.Logging`, ModelContextProtocol SDK 2.x |
| Deployment context | Foreign-CWD (launched inside other repos' sessions) · published (`roslyn-mcp@roslyn-mcp-marketplace`, public repo) → contract-care; single-host (no fleet) |
| Live verification | PERFORMED. Debug build of HEAD into scratchpad, driven over raw JSON-RPC (`harness.mjs`), sink env `ROSLYNMCP_OBSERVABILITY_SINK=file`, `XDG_STATE_HOME` redirected into this report dir. Two sessions (pid 45020 default verbosity; pid 41544 `Logging__LogLevel__Default=Debug`). |
| Not exercised | An unexpected-error (InternalError) path — could not be provoked with bad input; covered by static read + `ServerObservabilitySinkTests` only. |

## Surface inventory (§4.2)

| Probe | Result |
|---|---|
| Direct `ILogger.Log*` call sites | 197 (+10 `LoggerMessage.Define`) — all use message templates (structured at code level) |
| Direct calls with no EventId (→ eventId 0) | 51 |
| EventId collisions | `EventId(1, …)` used by 6 unrelated events (CodeActionService, TransportDisconnectDiagnostics, GatedCommandExecutor, DotnetCommandRunner, SupportedFixEnumerationService, WorkspaceManager) |
| Deliberate codes | 1001 UnexpectedFailure; 2101–2104 tool completed/cancelled/input-required/failed |
| Bypass writes (`Console.*`) | 5, all last-resort stderr notices (placeholder warning, sink-failure notices, script-worker crash line) — acceptable, none on stdout |
| Swallowed catches | 7 empty/comment-only sites inspected; all narrow exception filters with a justification comment — pass |
| Correlation | `RequestCorrelationContext` (AsyncLocal) → scope `correlationId=…` → `correlationId` field; same id appears in public error envelopes |
| Timings | `Stopwatch` in dispatch; `elapsedMs` in prose + response `_meta.{queuedMs,heldMs,elapsedMs}` |
| Health | `server_heartbeat`, `server_info`, `workspace_support_bundle` (headless, no workspace needed) |
| Vitals | none (no GC/WorkingSet/ThreadPool/Meter references in `src/`) |
| Sinks | stderr (Simple console, prose) always; JSON-lines file opt-in; structured-failure stderr opt-in |
| Schema drift test | `ServerObservabilitySinkTests.FileProvider_WritesScopedJsonLinesAndRotatesWithoutExceptionDetail` pins field *values* for a synthetic record; no full-key-set or event-code pin |

## Rubric (§4.3)

| # | Dim | Score | Evidence |
|---|---|---|---|
| A1 | Structured events | partial | Records are JSON but semantics live in prose `message`; provider drops ILogger state (`JsonLinesFileLoggerProvider.cs:78`). Key set observed: `ts,level,category,eventId,message,correlationId` (59/59 records). |
| A2 | Event vocabulary | fail | 51 sites eventId 0; six events share id 1; names not emitted; no catalog. |
| A3 | Correlation | pass | V2/V7: one id joins tool record + WorkspaceManager/GatedCommandExecutor/NuGet HttpClient records. Minor: SDK transport records have null id (Info digest). |
| A4 | Error payload | partial | Expected failures: `outcome=expected-error` only. Unexpected: exception types + frame count, no frames (deliberate secret-safety). No swallowed-catch defects. |
| A5 | Timings as data | partial | `elapsedMs` present but inside `message`; V3 needed regex. |
| A6 | Reachable sink | partial | File sink good (stable `%LOCALAPPDATA%/roslyn-mcp/logs/`, never CWD) but **off by default**; plugin `mcp.json` doesn't enable it; default stderr is host-swallowed. |
| A7 | Runtime verbosity | pass | V4: `Logging__LogLevel__Default=Debug` → 23 Debug records vs 0; documented (`docs/stdio-client-integration.md:38`). Little project-level Debug instrumentation (2 of 23). |
| A8 | Timestamps | pass | `2026-09-30T18:41:21.120Z` ISO-8601 UTC ms. |
| A9 | Stream discipline | pass | Harness saw zero non-JSON stdout lines across both sessions; logs on stderr + file. |
| A10 | Discoverability | partial | Consumer doc has path/sink/verbosity/fields; lacks filename pattern, recipes, code table; `AGENTS.md` has no section. |
| A11 | Health + vitals | partial | Heartbeat/info/support-bundle pass; vitals absent. |
| A12 | Schema durability | partial | Drift test exists (value-level); rotation deletes the prior segment; dead-pid files never pruned. |
| A13 | Dependency boundaries | partial | Correlation + target + exit code present; no duration; no workspace-load boundary record. |

## Live verification evidence (§4.4)

All commands run from `ai_docs/audits/20260930-1340/`; `L=state/roslyn-mcp/logs/roslyn-mcp-45020.jsonl`.

| # | Result | Command → captured output |
|---|---|---|
| V1 | PASS | `jq -c 'select(.message\|test("symbol_search"))' $L` → `{"ts":"2026-09-30T18:41:26.390Z","level":"Information","category":"RoslynMcp.StructuredCallToolFilter","eventId":2101,"message":"Tool symbol_search completed; outcome=success; elapsedMs=1389","correlationId":"c8d87d8c…"}` |
| V2 | PARTIAL | Correlation query for the failing `find_references` (`jq -c --arg c "$CID" 'select(.correlationId==$c)' $L`, `evidence/V2-chain.jsonl`) returns ONE record: `Tool find_references failed; outcome=expected-error; elapsedMs=1`. The client envelope (`evidence/run1.transcript.json`) carries `category: NotFound` + message; the log carries neither. |
| V3 | PARTIAL | p95 source only via regex: `jq -r 'select(.eventId==2101)\|.message\|capture("elapsedMs=(?<ms>[0-9]+)").ms' $L` → `77`, `1389` (symbol_search). Works, but parses prose. |
| V4 | PASS | `jq -r .level roslyn-mcp-41544.jsonl \| sort \| uniq -c` → `23 Debug 33 Information 3 Warning`; default-verbosity session: `53 Information 6 Warning`. |
| V5 | PASS | `server_heartbeat` → `connection{state,loadedWorkspaceCount,stdioPid,serverStartedAt}` + `_meta{queuedMs,heldMs,elapsedMs}` (`evidence/run1.transcript.json`). No vitals. |
| V6 | PASS | After the second process ran, `ls state/roslyn-mcp/logs` still lists `roslyn-mcp-45020.jsonl` (prior session) beside `roslyn-mcp-41544.jsonl`. Not tested: >10 MiB rotation destroying the prior segment (static: `JsonLinesFileLoggerProvider.cs:160`). |
| V7 | PARTIAL | `build_workspace`: `{"eventId":1,"category":"…GatedCommandExecutor","message":"Executed dotnet command for …SampleSolution.slnx: build … (ExitCode=0)","correlationId":"8da72a98…"}` joins `Tool build_workspace completed; outcome=success; elapsedMs=2220` on one id — but no duration on the boundary record. |

Secret scan of `evidence/` and `state/`: no credential patterns found. Paths and MSBuild diagnostic text appear in records (operator-local).

## Verdicts (§4.5)

| Verdict | Value | Justification |
|---|---|---|
| Sufficient (agentic)? | **conditional** | Causal-chain (A3) and verbosity (A7) work once the file sink is on. Blocking fix: `logging-default-sink-disabled-stderr-swallowed` (A6) — shipped default leaves no reachable sink. Also weak: A4 (expected-error records carry no category), A5/A1 (timings only in prose), A11 (no vitals), A13 (no boundary duration). |
| Durable? | **conditional** | Drift test and documented contract exist (A12/A10 partial), but event ids collide/are absent (A2) and rotation deletes evidence mid-repro. Blocking fix: `logging-event-id-collisions-no-catalog` + `logging-retention-destroys-repro-evidence`. |

## Proposed / filed rows

11 rows (`backlog=apply`), dependency order; fingerprints in `findings.json`. Nothing carried or reopened (no prior observability run).

| Row | Pri | Dim | Deps |
|---|---|---|---|
| `logging-jsonl-drops-structured-state` | Medium | A1/A5 | — |
| `logging-retention-destroys-repro-evidence` | Medium | A12 | — |
| `logging-default-sink-disabled-stderr-swallowed` | High | A6 | retention |
| `logging-event-id-collisions-no-catalog` | Medium | A2/A12 | fields |
| `logging-event-ids-remaining-call-sites` | Low | A2 | catalog |
| `logging-failure-records-lack-diagnosis` | Medium | A4 | fields |
| `logging-dependency-boundary-duration-missing` | Medium | A13 | fields |
| `logging-process-vitals-absent` | Medium | A11 | — |
| `logging-observability-contract-doc-incomplete` | Medium | A10 | fields, catalog |
| `logging-file-sink-sync-io-per-record` | Low | A5 | retention |
| `logging-info-observations` | Low (Info digest) | A3/A9/A10 | — |

Bad-code call-outs (Directive #3): all above are rows; additionally the `StderrServerObservabilitySink` and `FileServerObservabilitySink` emit two different schemas for the same `UnexpectedFailure` event (folded into the fields row).

Skipped/non-findings: A7, A8, A9 pass; no swallowed-exception defects; no secret in logs.

Dedup: existing row `logging-capability-parity` (MCP `logging` capability vs notifications) is adjacent, not a duplicate — it concerns the protocol surface, these rows the operator log stream. `logging-event-id-collisions-no-catalog` sized L by anchors (6 production files); expect the remediation engine to split it.
