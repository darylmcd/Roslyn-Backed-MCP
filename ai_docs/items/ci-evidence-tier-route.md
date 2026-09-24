# ci-evidence-tier-route — Add an evidence-only CI route and stop forcing CHANGELOG.md onto the full matrix

**row:** `ci-evidence-tier-route` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/resolve-ci-topology.ps1`
- `.github/workflows/ci.yml`
- `eng/verify-changelog-fragments.ps1`
- `CI_POLICY.md`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`
- (new) `tests/RoslynMcp.Tests/CiEvidenceTierConsumerContractTests.cs`

## Acceptance

- [ ] A PR changing only evidence paths (ai_docs/audits/**, ai_docs/reports/**, ai_docs/items/** except the fragment schema, audit-reports/** except the tracked promotion scorecard) routes `evidence`: no .NET build or test legs, one pwsh lint job, and the required `validate` check still reports success.
- [ ] Evidence paths mixed with docs route docs; mixed with code route code (highest tier wins; renames classify both names).
- [ ] A CHANGELOG-only PR routes docs, and the docs route runs the version-drift and breaking-version gates it previously skipped.
- [ ] audit-reports/** no longer requires a changelog fragment.
- [ ] A contract test fails when any test or eng/ script newly references an evidence-tier root without being classified.
- [ ] CI_POLICY.md documents the three tiers.

## Evidence

- PR #1607 (docs-only audit output) ran the full 6-shard matrix because ai_docs/**/*.txt and audit-reports/*.json are outside the docs pattern, and failed Linux leg 1 on a changelog-fragment demand for audit-reports/_latest-promotion-scorecard.json. Last 40 green PR runs: docs route 141–207 s, code route ~905 s median. — see `ai_docs/audits/20260924-1305/report.md`

## Context

- Tier consumers were verified 2026-09-24 by reading the tests. ReadmeSurfaceCountTests reads ai_docs/backlog.md, planning_index.md and plans/**. ShippedSkillBacklogCitationTests reads ai_docs/backlog.md. IssueTemplateAndLabelSeedTests reads ai_docs/items/backlog-d-fragment-schema.md. McpServerSurfaceTestSkillTests requires the scorecard to stay tracked. Those paths stay in the docs (contract) tier.
- Workflow-level paths-ignore is rejected: the required `validate` check would never report, so the PR would stay blocked. The skip must happen inside the workflow.
