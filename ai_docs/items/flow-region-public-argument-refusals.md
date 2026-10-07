# flow-region-public-argument-refusals — Publish flow-region argument corrections

**row:** `flow-region-public-argument-refusals` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:193`
- `tests/RoslynMcp.Tests/FlowAnalysisServiceTests.cs`
- (new) `tests/RoslynMcp.Tests/FlowArgumentRefusalWireTests.cs`

## Acceptance

- [ ] Both flow-analysis entry points publish authored, path-free corrections for invalid startLine/endLine/order with real parameter names, while preserving released category and BCL exception identity.
- [ ] Observe failing-before/passing-after production-boundary regressions for each shared ResolveAnalysisRegionAsync guard; retain valid data/control flow and safe treatment of genuine lower-layer Roslyn failures, including hostile lower-layer messages.

## Evidence

- At main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`, `FlowAnalysisService.cs:192-198` constructs plain ArgumentException values for invalid caller line bounds/order.
- Parent Acceptance claimed catches at lines 39/100 must preserve Public messages. Live source places caller guards in ResolveAnalysisRegionAsync before those try blocks; catches wrap genuine Roslyn analysis failures with FlowAnalysisFailurePolicy. Re-derive classification instead of publishing arbitrary compiler exception text.

## Context

- Split from `code-action-and-flow-argument-refusals-public-message`; one shared region-validation mechanism covers both entry points.
- Publication contract: `src/RoslynMcp.Core/Services/PublicArgumentException.cs`, `ArgumentErrors.cs`; check the current host boundary and `docs/release-policy.md`. Do not rely on the deleted core-move detail.
- backlog: sync ai_docs/backlog.md.
