# collector-diagnostics-bypass-error-formatter — Emit fail-closed diagnostics as plain stderr lines

**row:** `collector-diagnostics-bypass-error-formatter` · **pri:** `Low` · **size:** `M` · **deps:** `—`

## Anchors

- `eng/collect-hosted-shard-timings.ps1`
- `eng/summarize-test-results.ps1`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] A script-scope `trap` writes `$_.Exception.Message` to stderr and exits 1, so no ConciseView source echo, squiggle, or console-width-dependent wrap appears in the output.
- [ ] `TestResultsSummaryContractTests` asserts the exact one-line diagnostic without applying `NormalizeConsoleDiagnostic` to the expected side.
- [ ] Whether `eng/summarize-test-results.ps1` adopts the same shape is decided explicitly, not left implicit.

## Evidence

Reproduced by the cold reviewer of PR #1427 with `pwsh -NoProfile -NonInteractive -File` and a
redirected stream: an uncaught `throw` renders four formatter lines (`Exception:`, `Line |`,
`   N | <source>`, `     | ~~~~`) before the message, wrapped at the host console width. That width
dependence is what turned a green Windows gate into a red `ubuntu-latest` leg on run 33690371287.

The script already writes its report through `[Console]::Out.WriteLine`, so the plain-stream path
exists; only the failure path goes through the error formatter.

## Context

PR #1427 fixed the CI failure test-side, by normalizing the rendered output before matching. That is
correct and verified against real captured output, but it treats the symptom: the script is still
emitting operator-facing diagnostics through a formatter whose layout depends on the terminal.

Deferred from PR #1427 deliberately, with reasons: the change reshapes output for all nine collector
fail-closed assertions, and leaves the summarizer's three assertions on the formatter unless
`eng/summarize-test-results.ps1` is also edited — a fourth production file in an initiative already
at 3 of the Rule 3 cap of 4.

A side benefit: removing the source echo removes the pre-existing hazard that a fail-closed assertion
can be satisfied by the text of the `throw` statement rather than by the message it produced.

[source: 2026-09-02 backlog-remediate PR #1427 cold review]
