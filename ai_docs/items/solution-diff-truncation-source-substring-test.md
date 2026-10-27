# solution-diff-truncation-source-substring-test — Assert real omitted-file preview behavior

**row:** `solution-diff-truncation-source-substring-test` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/P4BehavioralBundleTests.cs:234`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`

## Acceptance

- [ ] Replace source substring proof with real changed-solution omitted-file/count behavior, reusing the existing fixture.
- [ ] Observe the behavioral oracle fail on broken marker behavior; preserve output limits and existing assertions.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. The retained finding identifies a test reading SolutionDiffHelper.cs for Omitted file(s). Retained P4 patch only removes no-op class cleanup, so the test-quality finding itself remains unimplemented.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
