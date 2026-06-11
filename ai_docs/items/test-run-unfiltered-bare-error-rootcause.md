# test-run-unfiltered-bare-error-rootcause — root-cause the bare "An error occurred invoking test_run"

**row:** `test-run-unfiltered-bare-error-rootcause` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:146` (`test_run`)
- test-runner service under `src/RoslynMcp.Roslyn/Services/`

## Acceptance

- [ ] Root cause determined FIRST: (a) a large *successful* TRX result exceeding the MCP output cap → add a `summary`/pagination mode (same payload-budget family as `test-discover-no-autopagination` + `project-diagnostics-no-summary-pages-out`), or (b) an exception/cancellation escaping `ClassifyAndFormat` → widen the envelope path
- [ ] Fix shipped per the determined cause
- [ ] Regression: fixture on a large unfiltered suite asserts a structured response (summary/pagination or envelope), not a bare invocation error

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 8, server v2.3.1. **Second repro (2026-06-08 retro):** see `ai_docs/reports/20260608T203050Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a#test_run. Source: 2026-05-31 surface-test + 2026-06-08 retro.

## Context

A full-suite (no-filter) `test_run` returned a bare "An error occurred invoking test_run" at server v2.3.1 even though the timeout/file-lock/build-failure `FailureEnvelope` (shipped via #108) is in place and the tool catches all exceptions (`ValidationTools.cs:178` → `ToolErrorHandler.ClassifyAndFormat`); the filtered `test_run` and `validate_workspace(runTests=true)` both worked (50/50 green).

NB: #611's original timeout-envelope ask is SHIPPED (#108) and its issue is closed — this is the distinct residual. **Second repro:** a COMPOUND `|`-OR filter (4 `FullyQualifiedName~` clauses) hit the same bare error at v2.3.0 (session b70ec703, 2026-05-27), while splitting it into 4 single-clause `test_run` calls each returned cleanly — supports hypothesis (a) (combined TRX output exceeds the MCP cap) over (b), and shows the bug is NOT unique to the unfiltered call.
