# suppression-argument-refusals-public-message — Pragma/suppression argument refusals (diagnosticId, line, filePath) are Public

**row:** `suppression-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SuppressionTools.cs`
- `tests/RoslynMcp.Tests/PragmaScopeManipulationTests.cs`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs`
- `tests/RoslynMcp.Tests/SuppressionToolsTests.cs`

## Acceptance

- [ ] All six diagnosticId/filePath/positive-line guards return fixed named public guidance and retain wire BCL identities where unchanged.
- [ ] Null, blank and unsupported severity values refuse before editorconfig or edit dispatch; supported trimmed case-insensitive error/warning/suggestion/silent/none values remain accepted.
- [ ] Main tool and parameter descriptions agree on supported values; actual service and dual-protocol raw-wire regressions prove zero mutation, secret exclusion and category migration.
- [ ] Record ADR 0019 and Changed - BREAKING migration; preserve internal logger/canonicalWritePath guards and pinned-root protection.

## Evidence

- 6 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.

| Re-vet 2026-10-07 | Evidence |
|---|---|
| Immutable base | `bc6e1f160fb9825127ba35386a37b6cae02523b0` |
| Complete producer defect | Six guards: `SuppressionService.cs:66,103,108,390,394,398`; unchecked `severity.Trim`:70 |
| Guidance discrepancy | `SuppressionTools.cs:20` omits supported `error`; parameter description :26 includes all five values |
| Semantic fanout | Live Roslyn ready, zero diagnostics; `ValidateVerifyWidenArgs` has two same-service references :183,:269 |
| Required correction | Stanza amendments1-2: severity category/write correction, actual-service dual-era wire proof, ADR0019, major migration; same selected row |
