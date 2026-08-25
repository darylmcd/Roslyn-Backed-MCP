# changed-format-gate-diagnostic-id-contract — align the changed-file formatter gate's id contract with its behavior

**row:** `changed-format-gate-diagnostic-id-contract` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-changed-format.ps1:77`
- `eng/verify-changed-format.ps1:214`
- `CI_POLICY.md`

## Acceptance

- [ ] `$gatedDiagnosticIds` either filters the observed findings, or is renamed (e.g. `$documentedDiagnosticIds`) so no reader believes a four-id filter exists.
- [ ] The script header, the `CI_POLICY.md` row, the failure message, and the changelog state the same id set the code actually enforces.
- [ ] A test pins the chosen behavior using a non-listed id (e.g. a CS compiler error line on a changed file).

## Evidence

Traced in the PR #1356 diff: `$gatedDiagnosticIds = @("FINALNEWLINE","IDE1006","IMPORTS","WHITESPACE")` is declared at line 77 and referenced only inside the failure string at line 293. The parse loop at 214-241 applies no id predicate, so any regex-matching `error|warning <id>` line on a changed file is bucketed and can fail the gate — including CS compiler errors. The changelog and CI_POLICY both assert the gate fails on the four named ids.

## Context

Surfaced by cold code-quality review of `format-changed-file-gate` (PR #1356, sweep `20260825T151721Z`). Advisory medium — the gate is fail-closed, so the mismatch is over-strict rather than unsafe, but the documented contract is wrong.
