# gate-timing-tests-fail-under-machine-load — Make wall-clock-bound tests deterministic under full-suite load

**row:** `gate-timing-tests-fail-under-machine-load` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:433`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs:115`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs:50`

## Acceptance

- [ ] `DocsOnlyAllowlist_Unusable_FallsBackToFullSuiteWithWarningAsync` no longer depends on a 30 s pwsh wall-clock limit that a loaded host exceeds (33 s observed).
- [ ] `CompilationCompleteness_PreservesFailuresAndScope` (git-scope arm) is deterministic under load (36 s run, `ChangedFilePaths` element count mismatch at line 50).
- [ ] Red-first: each fix is demonstrated against a simulated slow host or an injected clock/timeout, not a retry.

## Evidence

- Full local gate (`verify-release.ps1`, 3.5k tests) during backlog-remediate 20261001T130338Z failed with a DIFFERENT timing-sensitive test each run while the host sat at ~84% CPU from sibling sessions: `CiTopologyDecisionContractTests.DocsOnlyAllowlist_Unusable_FallsBackToFullSuiteWithWarningAsync` (`System.TimeoutException: docs-only allowlist resolver timed out after 30 seconds`, 33 s), `WorkspaceValidationVerdictTests.CompilationCompleteness_PreservesFailuresAndScope` (36 s, `CollectionAssert.AreEqual failed. Different number of elements`, test line 50), and a process-runner cancellation test (`RunAsync_ExitedParentWithInheritedPipes_StillHonorsCancellation`, 50 s, `The runner itself must finish before the test's safety timeout`). The first two pass in isolation (11/11 and 151/151); GitHub CI passed the same trees.

## Context

- Found while landing gen 1 of that plan; the run switched to GitHub `validate` as the gate. Do not add these to `known-flakes.md`; fix the wall-clock coupling. Third test (`RunAsync_ExitedParentWithInheritedPipes_StillHonorsCancellation`) has no anchor above because its file was not opened this session: locate it with `rg` first.
