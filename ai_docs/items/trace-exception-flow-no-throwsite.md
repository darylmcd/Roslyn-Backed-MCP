# trace-exception-flow-no-throwsite — add throw-site half + ranked type-specific catches

**row:** `trace-exception-flow-no-throwsite` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ExceptionFlowTools.cs:19`

## Acceptance

- [ ] Throw-site half added; cap raised or declared (return count-of-omitted); type-specific catches ranked above base-Exception catches
- [ ] Regression: fixture asserts throw-sites are returned and type-specific catches rank above base catches

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 4 (gates promotion), server v2.3.1. Source: 2026-05-31 surface-test.

## Context

`trace_exception_flow` returns only broad `catch(Exception)` sites and returned an identical 20-site list for two unrelated exception types (XmlException vs InvalidOperationException, all `catchesBaseException:true`), with `truncated:true` at the default cap and no throw-site / unhandled-at-boundary half of the analysis. The truncation hides whether any type-specific catch exists; the missing throw-site pairing limits usefulness for tracing a specific exception.
