# gated-build-command-budget — Bound build_workspace/build_project by BuildTimeout, not the 2-minute gate timeout

**row:** `gated-build-command-budget` · **pri:** `High` · **size:** `M` · **deps:** `workspace-restore-safe-execution`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:33`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:63`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32-75`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:73-101`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:216`
- (new) `tests/RoslynMcp.Tests/GateOperationDeadlineTests.cs`

## Acceptance

- [ ] `build_workspace` and `build_project` are bounded by their own `BuildTimeout` (5 minutes), not the 2-minute gate `RequestTimeout`. Acquisition (rate limiter, global throttle, per-workspace lock, auto-reload) stays bounded by `RequestTimeout` and is never charged to the command budget.
- [ ] The command phase holds neither the per-workspace reader/writer lock nor a global throttle slot. The tool resolves its inputs under the gate, releases it, and runs the dotnet command through `GatedCommandExecutor`, whose global and per-workspace command gates already serialize commands. It re-enters the read gate only for post-run work that reads the workspace (build diagnostic span enrichment, `BuildService.cs:42`); a workspace closed or reloaded mid-command degrades to un-enriched output and says so.
- [ ] The command records the workspace version when it starts (`IWorkspaceManager.GetCurrentVersion`). If the version moved before post-run enrichment, the result carries an additive warning `workspaceChangedDuringRun`; a run with no intervening change carries none.
- [ ] `_meta.gate` keeps reporting `queuedMs` and `heldMs` for the gated phases and the result reports the command phase duration separately, so a caller can tell a lock wait from a slow command. Additive only (`docs/release-policy.md` treats additive status fields as minor-compatible).
- [ ] Red-first tests (fake clock or fake runners): (a) a gated `build_workspace` whose command runs 3 minutes completes (today cancelled at 120 s); (b) a writer that arrives 30 s into the build gets its lock without waiting for the build, and the build's result carries `workspaceChangedDuringRun` once that writer applied a change.
- [ ] `RequestTimeout` keeps its default and still bounds every other tool; raising it globally is not the fix. A command-budget expiry keeps category `Timeout` (`GatedCommandExecutor.cs:95-101`).

## Evidence

- `WorkspaceExecutionGate.cs:216` arms `new CancellationTokenSource(_requestTimeout, _timeProvider)` before throttle and lock waits and links it into the action token; `ExecutionGateOptions.cs:52` defaults it to 2 minutes. The inner `BuildTimeout` already exists (`GatedCommandExecutor.cs:74-75`) but runs inside the outer gate, so the request deadline always fires first.
- Retro 2026-09-27: gated tool calls time out at about 120 s while the same command through the CLI finishes in about a minute (20 occurrences, 4 repos).

## Context

- First seam of split parent `gated-build-test-operation-deadline`; it establishes the release-then-execute pattern the sibling seams reuse (`gated-test-run-command-budget`, `gated-validate-test-phase-budget`, `gated-vuln-scan-command-budget`).
- The parent's restore command-gate requirement (route load/reload restore through the per-workspace command gate) is owned by `workspace-restore-safe-execution` and must land before this row releases the RW lock during a build, hence the dep.
- No change to `IWorkspaceExecutionGate` (23 test classes implement it); prefer default-implemented members over signature changes on service interfaces.
