# elicitation-choiceprompt-test-suite-consolidation — three suites pin the same collapsed forwarder

## Anchors

- `tests/RoslynMcp.Tests/ElicitationAllowlistPolicyTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`

## Acceptance

- [ ] The three `HasElicitation` cases (capability present / null capabilities / capability omitted) exist exactly once, in a suite named for `ElicitationChoicePrompt`; redundant copies deleted and the surviving class docstrings no longer explain a cross-type mismatch.
- [ ] The now-dead `using RoslynMcp.Host.Stdio.Middleware;` in `SymbolDisambiguationElicitationTests.cs` is removed, and every remaining using in the touched suites is verified consumed.

## Evidence

Traced during the PR #1237 review: after the forwarder collapse, all 8 `HasElicitation` call sites across the three suites resolve to the single definition on `ElicitationChoicePrompt`, so the pre-diff justification (each suite exercised a distinct forwarder) no longer holds — the diff's own new docstring drops `HasElicitation` from the "remaining delegates" list. Separately, enumerating all 8 types in `RoslynMcp.Host.Stdio.Middleware` and grepping `SymbolDisambiguationElicitationTests.cs` returns zero hits, proving its Middleware using is dead.

The plan stanza for that initiative explicitly deferred this dedup to "its own backlog row".
