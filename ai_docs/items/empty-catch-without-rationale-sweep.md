# empty-catch-without-rationale-sweep — Add rationale or narrow 10 empty catches

**row:** `empty-catch-without-rationale-sweep` · **pri:** `Low` · **size:** `S`


## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs`

## Acceptance

- [ ] Each of the 10 sites has a rationale comment, a narrower exception filter, or an asserted outcome.

## Evidence

Sites: src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:875 (filtered cancellation catch); tests AnalyzerShadowLeaseNoFollowTests:293, DotnetCommandRunnerPipeLifetimeTests:210, FileEditsDtoTests:67, TestTempRoot:97, ForkNativeAdmissionTests:385 and 386, ForkNativeOwnershipTests:519, WorkspaceForkGraphReplayTests:404, WorkspaceForkSourceLifecycleTests:481. 62 empty catches scanned, 52 already carry rationale comments. (doc-audit 2026-11-17)
