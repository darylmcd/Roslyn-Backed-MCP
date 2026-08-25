# Logging & Diagnostics Surface Audit — Roslyn-Backed-MCP (2026-08-25)

Run: `/logging-audit C:/Code-Repo/Roslyn-Backed-MCP` · mode=full · HEAD `72636987` · binary `artifacts/publish/host-stdio` (same-day build, version `4.0.0+58af800b`). Structured detail: `findings.json` (same dir). Captures: `run1-*` (baseline + observability sink), `run2-*` (Debug verbosity + sanctioned roots + workspace load).

## Repository summary

| | |
|---|---|
| App class | MCP server (stdio host) |
| Deployment context | **published** (`roslyn-mcp@roslyn-mcp-marketplace`, MCP Registry `io.github.darylmcd/roslyn-mcp`) + **foreign-CWD** (runs inside consumers' sessions) |
| Sinks | stderr only (prose console, no timestamps); JSON sink = unexpected-exception-only, default `Disabled` (`ROSLYNMCP_OBSERVABILITY_SINK`) |
| Call sites | 410 sanctioned `ILogger` lines / 82 files; **0 stdout bypasses** (analyzer-enforced) |
| In-band | `_meta` gate metrics (elapsedMs/queuedMs/heldMs) on every tool response; `server_info` / `server_heartbeat` / `workspace_support_bundle` |
| Degradations | none — full live verification performed |

## Rubric (A1–A13)

| Dim | Score | One-line evidence |
|---|---|---|
| A1 structured | **fail** | stream is prose `info: Cat[0]` lines; JSON sink exception-only + default-off |
| A2 vocabulary | partial | SDK EventIds present; app records mostly `[0]` |
| A3 correlation | partial | correlation id on the failure template ONLY; zero `BeginScope`/`IncludeScopes` repo-wide |
| A4 error payload | partial-pass | in-band envelopes w/ category+corrective excellent; stream failure line detail-free |
| A5 timings | partial | per-call `elapsedMs` in-band `_meta`; stream durations = SDK prose, request-keyed |
| A6 reachable sink | **fail** | stderr swallowed by hosts; no file sink; foreign-CWD run wrote nothing to CWD (clean) |
| A7 verbosity | **pass** | `Logging__LogLevel__Default=Debug` → 68→202 lines, no rebuild — but undocumented |
| A8 timestamps | **fail** | zero timestamps in captured stream |
| A9 stream discipline | **pass** | stdout protocol-pure, `StdoutWriteAnalyzer`-enforced |
| A10 discoverability | partial | runtime.md partial (AI-facing); README none; 3 docs carry corrupted `n=stderr` env name |
| A11 health+vitals | partial-pass | server_info/heartbeat/support-bundle strong; no process vitals |
| A12 durability | partial | stdout purity IS an enforced contract; record schema is not; nothing retained |
| A13 dependency boundary | partial | HTTP boundary fully logged w/ durations; dotnet-CLI duration in-band only |

## Live verification (V1–V7)

V1 partial (records only via driver-redirected capture) · V2 partial (`rg <correlationId>` → exactly 1 record, no chain) · V3 fail (no structured durations in stream) · **V4 pass** (env-var verbosity flip, live) · **V5 pass** (server_info: version+git-sha, tiers, parity) · V6 fail-by-construction (nothing persists) · V7 partial (workspace/HTTP boundaries logged; no structured boundary events; 25+ repeated shadow-sweep dbug lines).

## Verdicts

| Verdict | Value | Blocking fix |
|---|---|---|
| **Sufficient (agentic)?** | **conditional** | A6 — an agent mid-session has NO greppable telemetry (in-band `_meta`/server_info are genuinely strong for single-call reasoning; post-hoc debugging and cross-call perf fail). Fix = the file sink row. |
| **Durable?** | **conditional** | A12 — stdout purity is analyzer-enforced (best-in-class), but record schema isn't a contract and nothing persists to retain. |

## Rows filed (see backlog)

| pri | id | dim |
|---|---|---|
| High | `observability-file-sink-full-stream` | A6/A1 |
| Medium | `tool-call-stream-record-enrichment` | A3/A5/A8 |
| Medium | `observability-consumer-doc-contract` | A10/A7 |
| Medium | `docs-observability-sink-env-name-corruption` | A10 |
| Low | `backlog-dangling-deps-closed-observability-rows` | hygiene |

Info observations (no rows): shadow-sweep dbug spam (aggregate to one record); vitals absent from heartbeat (EventCounters candidate); sink default `Disabled` is an open product decision; broader A2 event-id catalog deferred.

Contract note: repo is the PUBLIC exception — all proposed changes are additive/opt-in env + doc surfaces (no ADR needed); PR #1253/ADR 0003 deliberately retired MCP protocol logging, and every row here stays on the operator-side sink path.
