# missing-root-retirement-provider-dispose-timeout-flake — ProviderDispose_LeavesLateRootRetirementToManagerShutdown times out on hosted Windows

**row:** `missing-root-retirement-provider-dispose-timeout-flake` · **pri:** `Medium` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs:262-266`
- `ai_docs/known-flakes.md`

## Acceptance

- [ ] Root cause of the 30 s guard expiry identified (real MSBuild solution load under shared-runner load vs a hang) with evidence from a hosted re-run.
- [ ] Fix removes the load-sensitivity without weakening the assertion; known-flakes.md updated if it remains a registered flake.

## Evidence

- Failed in ci.yml run 44273173566 (job 134694729792, windows-hosted-6-of-6) on an unrelated PR; not in known-flakes.md; 8 other recent failed runs do not show it. The test uses `new CancellationTokenSource(TimeSpan.FromSeconds(30))` around a real sample-solution load. Sibling ProviderDispose_Drains... timed out at 30 s under local load too (shared-expr report). Also see existing row timing-test-reaudit-wave (broader).
