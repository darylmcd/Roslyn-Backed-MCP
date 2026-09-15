# changed-format-gate-endofline-coverage

**row:** `changed-format-gate-endofline-coverage` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/verify-changed-format.ps1` — gatedDiagnosticIds filtering.
- `eng/format-diagnostic-contract.ps1` — shared formatter grammar.
- `tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs` — script contract tests.

## Acceptance

- [ ] Include ENDOFLINE in the changed-file formatter contract so CRLF changes cannot pass while the full baseline generator rejects them.
- [ ] Keep existing baseline allowances and unrelated analyzer ownership unchanged.
- [ ] Add one regression with a controlled ENDOFLINE formatter result and prove the changed-file gate fails with the affected path.

## Evidence

2026-09-15: verify-changed-format passed three edited CRLF C# files; FormatterBaselineContractTests rejected two new paths. Full formatter report identified ENDOFLINE, which the changed-file gate silently excludes from its allowlist. Normalizing the local files to the repository LF policy fixes the introduced bytes; this row tracks the early-gate gap.
