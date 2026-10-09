| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `Top10V2RegressionTests.cs:175-195` creates an isolated workspace but claims shared ownership and accepts rolled_back as success. A real content change and exact outcome/content assertions are missing. |
| Approach | - [ ] Arrange a clean isolated workspace and a preview with a deterministic real content change.<br>- [ ] Require exact `applied` outcome, verify intended edited content and reported applied files; reject `rolled_back`.<br>- [ ] Replace the shared-workspace comment with the actual isolated ownership contract.<br>- [ ] Observe a regression fail when a clean apply incorrectly returns rollback, then pass with the correct implementation/fixture.<br>- [ ] Run the complete Top10V2RegressionTests class and relevant apply-with-verify companion tests; retain rollback and pre-existing-error coverage. |
| Scope | Production 0: none. Tests 1: `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs`. Own fragment; no deletions. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Keep pre-existing-error and genuine rollback cases separate. Observe the strengthened contract reject a controlled rolled_back result during a temporary mutation probe; do not introduce a production rollback bug to manufacture a red test. ApplyWithVerifyTool is a read/validation anchor; modify production only if the new test exposes a real defect, then widen Scope with evidence. |
| Validation | Run Top10V2RegressionTests and apply-with-verify companions. Pin a deterministic preview delta, exact applied status, reported files and actual post-apply bytes; prove rollback violates this success contract with a temporary mutation probe. Per-edit compile_check and targeted test_run; scoped executor gate; required hosted validate per CI_POLICY.md is the full landing gate, without duplicating hosted checks locally. |
| Performance review | N/A - test-only contract strengthening; no production performance change. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | The clean apply-with-verify regression requires a real applied edit and verifies its resulting content. |
| Backlog sync | Close rows: [top10-good-preview-result-contract]. |
