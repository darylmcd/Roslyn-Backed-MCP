# test-trx-verdict-consistency — Reject inconsistent and unsuccessful test-report proof

**row:** `test-trx-verdict-consistency` · **pri:** `Medium` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:135`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:279`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:329`
- `docs/decisions/0010-validation-verdict-completeness.md`

## Acceptance

- [ ] Validate report structure, counters and outcomes before aggregation; preserve legitimate zero-test and partial result shapes.
- [ ] An exit-zero process cannot make adverse/incomplete reports successful; reject invalid/negative/overflowing counters and contradictory proof.
- [ ] Use real report fixtures for old-red/new-green. Correct inconsistent passed-report fixtures while preserving separate successful process/failed report and nonzero process/passed report controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current parser takes the first Counters, converts parse failures to zero, sums unchecked, and derives failureEnvelope only from execution.Succeeded. Retained branch adds a distinct report-verdict mechanism and TestExecutionVerdict; no equivalent current-main row was found.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
