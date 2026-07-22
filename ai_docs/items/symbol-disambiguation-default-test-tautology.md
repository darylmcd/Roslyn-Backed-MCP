# symbol-disambiguation-default-test-tautology — Pin agent-first disambiguation default with a real assertion

**row:** `symbol-disambiguation-default-test-tautology` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs:87`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs:106`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:93`

## Acceptance

- [ ] A test reads the actual `allowElicitation` parameter default (e.g. via `ParameterInfo.DefaultValue`) so flipping the signature default to `true` fails the suite
- [ ] Or a behavioral test omits `allowElicitation` and asserts the agent-first candidate-list envelope is returned, removing reliance on the tautological `const && HasElicitation` assertions

## Evidence

- Traced during code-quality review of PR #1093 (`symbol-disambiguation-agent-first-default`): the two `AllowElicitationGate_*` tests assert on a locally-declared `const bool allowElicitationDefault = false`, not on `SymbolTools`; `false && anything` is trivially false and never observes the method's real default, so the regression the surrounding comment claims to guard against cannot actually be caught.

## Context

Follow-on from `symbol-disambiguation-agent-first-default` (PR #1093, 2026-07-22), which made symbol candidate selection agent-first by default. The regression test added alongside that change references a local constant instead of the real method default, so it would pass even if a future refactor silently restored the elicit-first default.
