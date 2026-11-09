# timeout-command-restore-timer-identity — Identify command and restore timers

**row:** `timeout-command-restore-timer-identity` · **pri:** `Medium` · **size:** `L` · **deps:** `timeout-request-timer-envelope`

## Anchors

- `src/RoslynMcp.Core/Services/IGatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:57`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:148`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:250`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:777-816`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:23`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`

## Acceptance

- [ ] Every command budget carries an explicit timer identity from its owner; no identity is inferred from duration or argument text, and no default overload silently loses it.
- [ ] Build, test, vulnerability scan and fork restore timeouts name their own configured knob. `test_run`'s result envelope names `ROSLYNMCP_TEST_TIMEOUT_SECONDS` without leaking arguments.
- [ ] Restore planning, the fork restore CTS conversion at `WorkspaceForkApplyService.cs:813-816`, and native fork admission timeouts report the budget that actually expired, including an earlier request deadline. Preserve cancellation provenance. Cover pre-executor expiry and command expiry with red-first regressions.
- [ ] No untyped command or restore timeout is rendered as a misleading knob; retain category `Timeout`, existing `isError`, and wire exception type `TimeoutException`.

## Evidence

- `GatedCommandExecutor.cs:122` throws an untyped timeout shared by three command owners; `RestoreInvocationPlanner.cs:23,55,62,100,105` can throw before executor entry. Current gate and handler flatten these sources into generic advice.

## Context

- Depends on the typed wire contract from `timeout-request-timer-envelope`. This is a tracked remainder, not yet selected for code; split further into independently correct slices before implementation because its production anchors span more than four files. Check `ForkAdmissionMaterial.cs:190-211` and `ForkAdmissionTask.cs:62` during that split.
