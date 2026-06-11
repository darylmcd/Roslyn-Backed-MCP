# test-discover-no-autopagination — unfiltered test_discover pages out, add pagination/summary

**row:** `test-discover-no-autopagination` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:54`
- test-discovery service under `src/RoslynMcp.Roslyn/Services/`

## Acceptance

- [ ] Offset/limit pagination (or a `summary` mode returning counts + per-project rollup) added so the unfiltered call degrades gracefully
- [ ] Regression: fixture on a large suite with no filter asserts paginated/summary output, no hard error

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 8, server v2.3.1. Source: 2026-05-31 surface-test.

## Context

`test_discover` with no filter on a 236-test suite returned an ~85KB / 1227-line payload that exceeded the MCP token cap and hard-errored — no offset/limit/auto-pagination (the tool self-documents BUG-007 "needs projectName/nameFilter" but the default call is unusable). Same payload-budget family as `project-diagnostics-no-summary-pages-out` and `find-duplicated-methods-no-byte-budget`. Distinct from CLOSED #752 (FQDN-filter zero-hits).
