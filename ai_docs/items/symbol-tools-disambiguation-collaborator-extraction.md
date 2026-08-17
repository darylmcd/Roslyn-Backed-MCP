# symbol-tools-disambiguation-collaborator-extraction — Extract symbol disambiguation orchestration

**row:** `symbol-tools-disambiguation-collaborator-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `elicitation-choiceprompt-test-suite-consolidation`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` — `TryDisambiguateMetadataNameAsync` and its candidate-selection helpers.
- New focused collaborator under `src/RoslynMcp.Host.Stdio/Elicitation/`.
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`
- `tests/RoslynMcp.Tests/ElicitationChoicePromptTests.cs`

## Acceptance

- [ ] Move metadata-name candidate selection and elicitation orchestration out of `SymbolTools` without a forwarding duplicate.
- [ ] Preserve agent-first candidate lists, optional operator elicitation, cancellation, unsupported-client fallback, and selected-symbol semantics.
- [ ] A table-driven regression covers zero, one, and multiple candidates with elicitation accepted, declined, cancelled, and unsupported.
- [ ] `SymbolTools` retains only endpoint orchestration and delegates policy to the collaborator.

## Evidence

- This is the remaining SymbolTools responsibility from the oversized `host-tools-cohesion-split` row; the AdvancedAnalysisTools work is tracked by four dependency-ordered extraction rows.
