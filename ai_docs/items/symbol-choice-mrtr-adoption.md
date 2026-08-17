# symbol-choice-mrtr-adoption — Move ambiguous-symbol choice to MRTR

**row:** `symbol-choice-mrtr-adoption` · **pri:** `High` · **size:** `M` · **deps:** `mcp-mrtr-dispatch-contract,workspace-path-mrtr-adoption`

## Anchors

- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`
- New `tests/RoslynMcp.Tests/SymbolDisambiguationMrtrWireTests.cs`.

## Acceptance

- [ ] `symbol_search`, `go_to_definition`, and `find_references` produce one request-scoped titled choice request for an ambiguous symbol with stable identifiers and no sensitive values.
- [ ] Accepted input selects exactly the intended symbol and retries once; unsupported, `allowElicitation=false`, rejected, malformed, and stale choices preserve the additive-list fallback without mutation.
- [ ] Apply and test an explicit bounded option cap before sending choices.
- [ ] Parameterize the same round trip for 2025-11-25 stateful compatibility and 2026-07-28 `server/discover` MRTR.
- [ ] Remove the direct `ElicitAsync` continuation for this workflow while preserving SDK request-scoped behavior.
- [ ] Preserve and migrate the existing cancellation regressions: cancellation during choice request, response consumption, or retry propagates unchanged.
- [ ] After `workspace-path-mrtr-adoption` removes the remaining shared legacy consumer, retire only obsolete non-cancellation `HasElicitation`/`ElicitAsync` assertions, migrate the two OCE regressions, and consolidate choice ownership in the MRTR suites; the two superseded test-hygiene rows stay closed.

## Evidence

- Current choice recovery is built around a direct nested `ElicitAsync` continuation; SDK 2.1 request-scoped capability handling is not identified as defective.
- SDK 2.1 makes input responses request-scoped specifically to support safe concurrent multi-round-trip calls.
