# top10-good-preview-result-contract — Pin a clean apply-with-verify success and content

**row:** `top10-good-preview-result-contract` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`

## Acceptance

- [ ] Arrange a clean isolated workspace and a preview with a deterministic real content change.
- [ ] Require exact `applied` outcome, verify intended edited content and reported applied files; reject `rolled_back`.
- [ ] Replace the shared-workspace comment with the actual isolated ownership contract.
- [ ] Observe a regression fail when a clean apply incorrectly returns rollback, then pass with the correct implementation/fixture.
- [ ] Run the complete Top10V2RegressionTests class and relevant apply-with-verify companion tests; retain rollback and pre-existing-error coverage.

## Evidence

| Evidence | Observation |
|---|---|
| PR1753 head 2da8e3497e1d8d04a2e3b564e9463f811e1d19ed, Top10V2RegressionTests:174-194 | GoodPreview_ReturnsApplied accepts applied or rolled_back; comment claims shared workspace despite CreateIsolatedWorkspaceAsync. |
| Immutable generation base a74d846295e45eac9630b405a1b374882d1968ed | Same permissive assertion/comment precede the resource changes. |
| ApplyWithVerifyTool:60-92 | Applied and RolledBack are distinct outcomes with separate status and payload contracts. |
| Independent mechanism | Resource argument publication does not change the apply/verify workflow; file this assertion defect separately. |

2026-10-09 planning-quality finding: this initiative performance cell contained copied alias-resolution text belonging to workspace-project-alias-lookup. Corrected through audited stanza-amend; source approach, runtime scope, acceptance and estimate unchanged. This existing row tracks implementation; no runtime completion claimed.
