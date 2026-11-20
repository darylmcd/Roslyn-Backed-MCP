# replace-usages-keep-unsupported-usage-concrete — Keep an unsupported usage concrete under replaceUsages

**row:** `replace-usages-keep-unsupported-usage-concrete` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ConcreteUsageRewriter.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` — replaceUsages rewrite and gate.
- `tests/RoslynMcp.Tests/InterfaceReplaceUsagesSpellingTests.cs` — `A_Member_Outside_The_Interface_Refuses_With_The_Gate_Text` pins today's refusal.

## Acceptance

- [ ] Operator picks the contract. If "keep concrete": a usage whose retype would not compile stays concrete, the rest are retyped, the preview lists the kept usages, and pending and applied compile; the pinning test is rewritten with a Migration note.
- [ ] If "refuse": close this row and keep the pinning test.

## Evidence

- iface-extraction report (PR 2145), sketch 1: "(M) Per-usage leave concrete instead of whole-preview refusal under replaceUsages".

## Notes

Operator decision 2026-11-20: keep concrete. A usage whose retype would not compile stays concrete, the rest are retyped, the preview lists the kept usages; rewrite the pinning test with a migration note.
