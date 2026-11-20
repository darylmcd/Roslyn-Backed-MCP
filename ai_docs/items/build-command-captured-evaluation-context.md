# build-command-captured-evaluation-context — Build with the loaded evaluation context

**row:** `build-command-captured-evaluation-context` · **pri:** `Medium` · **size:** `M` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BuildService.cs:46`
- `src/RoslynMcp.Core/Services/IBuildService.cs`

## Acceptance

- [ ] Capture actual loaded global properties in both project and workspace plans; retain that immutable context across execution/reload.
- [ ] Prove conditional source/reference build behavior under distinct configured sessions; preserve diagnostic identity/count/span enrichment and redact caller values after semantic parsing.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. PrepareWorkspaceBuildAsync and PrepareProjectBuild emit only build,target,--nologo; BuildCommandPlan has version but no evaluation globals. The retained item records this as unfinished, so branch existence is not delivery.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

Current BuildService capture owns version and target. Qualify both stable build routes with selected public load properties, scope epochs and public argv/diagnostic redaction, and distinguish private fork admission globals. If preserving existing stable fields and semantics is impossible, resume at the next operator-approved major with ADR, migration and both raw eras.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
