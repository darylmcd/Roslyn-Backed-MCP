# genericity-guard-nonmarkdown-shipped-files — shipped .ps1 files are outside the gate

## Anchors

- `eng/verify-skills-are-generic.ps1`
- `skills/mcp-server-surface-test/lib/render-finding.ps1`
- `skills/mcp-server-surface-test/scripts/archive-old-reports.ps1`

## Acceptance

- [ ] The gate scans every shipped file under `skills/` (or an explicit extension set including `.ps1`), not just `*.md`; the C# echo mirrors the same set.

## Evidence

Verified during the PR #1240 review: two `.ps1` files ship under `skills/` and are covered by neither the PowerShell gate nor the C# test. Both were grepped against the full banned list and are **clean today**, so this is a coverage gap rather than a live leak.

PR #1240's own rationale for widening `SKILL.md` -> `*.md` ("prompt bodies and READMEs ship to installers verbatim") applies verbatim to shipped scripts.
