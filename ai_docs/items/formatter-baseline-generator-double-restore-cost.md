# formatter-baseline-generator-double-restore-cost — Stop paying two full restores in the generator check

**row:** `formatter-baseline-generator-double-restore-cost` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`
- `eng/generate-format-baseline.ps1`

## Acceptance

- [ ] `RunGeneratorCheckAsync` stops paying a second full `dotnet restore`, OR the second restore is documented as required by the fail-closed truncation contract, with the reason stated at the call site.
- [ ] Whichever contract is chosen is pinned by an assertion rather than left to prose.

## Evidence

`RunGeneratorCheckAsync` invokes `eng/generate-format-baseline.ps1` without `-NoRestore`, so the test
pays a full `dotnet restore` on top of the one the generator already performs. Real cost driver in a
test that is otherwise bounded at five minutes.

## Context

Deferred deliberately by row `formatter-baseline-generator-concurrent-load-timeout` (shipped as
PR #1429): dropping the restore weakens the fail-closed truncation contract at
`eng/generate-format-baseline.ps1:103-106`, and it is exactly the "measured replacement" that row's
new success-path phase timings were added to justify. Those timings now accumulate in TRX output, so
the evidence this change needs is being collected.

**Filed late.** Both `ai_docs/items/formatter-baseline-generator-concurrent-load-timeout.md` and the
plan stanza for PR #1429 asserted this deferral was "tracked by row
`formatter-baseline-generator-double-restore-cost`" while no such row existed. Surfaced by the cold
reviewer of PR #1429. Under Directive #1 an untracked deferral does not exist; this row makes the
claim true.

[source: 2026-09-02 backlog-remediate PR #1429 cold review]
