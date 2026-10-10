# test-run-zero-match-filter-silent-success — Warn when a test_run filter matches zero tests

**row:** `test-run-zero-match-filter-silent-success` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`

## Acceptance

- [ ] test_run(filter matching nothing) returns a warning (or distinct status) naming the filter

## Evidence

- test_run(filter=NoMatch, compact) → total 0, succeeded true, no warning. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

2026-10-10 project-alias current-session witness:
- Immutable product base0442d5fa0d4be457f0f9a5f0de50996ada720750, TestRunnerService.cs238 directly returns `DotnetOutputParser.ParseTestRun(execution, trxFiles) with { CommandDurationMs = stopwatch.ElapsedMilliseconds }`; no zero-match warning is introduced at this return.
- Exact original MCP response recovered from the executor's own rollout, timestamp2026-10-10T03:42:09.563Z: `test_run` with nonempty filter `ClassName=RoslynMcp.Tests.WorkspaceProjectAliasLookupTests` and compact:true, before that fixture existed, returned exitCode0/succeededtrue/durationMs24154; total/passed/failed/skipped all0, failureEnvelope:null and no warning field.
- Sanitized original invocation, response and parsed result: <session-scratch>/project-alias/zero-match-original-response.json. This zero-run is a defect witness, never regression verification; later actual alias scopes contain tests and are retained separately.
- Amend this existing root-cause row; no duplicate or source implementation in this bookkeeping step. Require a nonempty zero-match filter warning or distinct status while retaining truthful process result and actual match counts.
