## Anchors

- `tests/RoslynMcp.Tests/ToolCallErrorWireContractTests.cs:97` — `correlationId` asserted only non-blank
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:169` — multi-workspace fast-fail, newly era-shaped, no wire coverage
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:188` — ambiguous auto-load fast-fail, same

## Acceptance

- A wire case drives the multi-workspace fast-fail AND the ambiguous auto-load fast-fail, asserting `resultType` is omitted on `2025-11-25` and present on `2026-07-28`.
- The `correlationId` assertion rejects the literal `"unavailable"` (or matches the generated id format), so a lost correlation context fails rather than passes.

## Evidence

From the Step 8b code-quality review of PR #1290. That PR routed three error-return paths through `ApplyProtocolResultShape`, but the new wire suite drives only the general-catch path (`:271`). The other two changed protocol-visible behavior with no wire coverage: existing tests (`StructuredCallToolFilterResolutionTests.cs:177`, `StructuredCallToolFilterAutoLoadTests.cs:62`) call `BuildErrorResult` in-process and never observe `ResultType`, so a regression on either line ships green.

Separately, `correlationId` is asserted only via `IsNullOrWhiteSpace`, which `ToolErrorHandler`'s literal `"unavailable"` fallback also satisfies.

The related dead-sentinel finding from the same review was FIXED in PR #1290 (commit `8d0fdd7c`) rather than deferred here, because a redaction assertion that cannot fail undermines the regression lock the initiative exists to provide.

Source: code-quality review of PR #1290, sweep 20260819T180531Z.
