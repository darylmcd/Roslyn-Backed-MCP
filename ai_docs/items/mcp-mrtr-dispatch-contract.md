# mcp-mrtr-dispatch-contract — Establish the SDK 2.1 MRTR dispatch contract

**row:** `mcp-mrtr-dispatch-contract` · **pri:** `High` · **size:** `M` · **deps:** `elicitation-coordinator-cancellation-propagation,tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- A focused request-scoped input adapter under `src/RoslynMcp.Host.Stdio/Elicitation/`.
- New `tests/RoslynMcp.Tests/McpMrtrWireContractTests.cs`.

## Acceptance

- [ ] `InputRequiredException` escapes ordinary tool-error conversion so the SDK emits the correct input-required result.
- [ ] Retry requests consume only their own `RequestContext<CallToolRequestParams>.Params.InputResponses`; no session/static capability or response cache is introduced.
- [ ] One synthetic form request completes a full initial-call/input-response/retry round trip under 2025-11-25 stateful compatibility and 2026-07-28 MRTR.
- [ ] Rejection and malformed input responses have explicit sanitized outcomes; `OperationCanceledException` from adapter dispatch/response/retry propagates unchanged, while `elicitation-coordinator-cancellation-propagation` owns the pre-MRTR coordinator catches.
- [ ] The adapter exposes a reusable policy boundary without coupling workspace and symbol-choice rules.
- [ ] Document why direct `ElicitAsync` remains stateful-only while MRTR is portable across the SDK 2.1 session modes.

## Evidence

- SDK 2.1 recommends `InputRequiredException` plus request-scoped `InputResponses` for portable server-driven input; the mechanism works under both protocol eras.
- Current recovery catches binder failures and initiates a direct nested `ElicitAsync` continuation; no claim that SDK 2.1 request capabilities are global is part of this row.
