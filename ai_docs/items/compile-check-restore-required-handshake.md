# compile-check-restore-required-handshake — compile_check answers an unrestored workspace with success:false and zero diagnostics; workspace_load never auto-restores

**row:** `compile-check-restore-required-handshake` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:20-75`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:40-92`
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs:25-43`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:218-268`
- `src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs:136-165`
- `src/RoslynMcp.Host.Stdio/Program.cs:240-260`
- `docs/decisions/README.md`
- `docs/product-contract.md`
- `ai_docs/references/environment-variables.md`
- (new) `tests/RoslynMcp.Tests/CompileCheckRestoreRequiredWireTests.cs`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadAutoRestoreTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs`

## Acceptance

- [ ] `compile_check` on a restore-required workspace returns a structured tool error instead of `success:false` with an empty diagnostic list. The error has `isError: true`, a new `RestoreRequired` error category (added to `ResourceReadResultFilter`'s map; `ResourceReadWireContractTests` requires every category to be mapped) and a message that names the one call that fixes it: `workspace_reload` with `autoRestore=true`. Red-first wire test through the tool.
- [ ] `compile_check` never restores or reloads itself. It is annotated `ReadOnly=true, Idempotent=true` (`CompileCheckTools.cs:27`) and runs under the per-workspace read lock (`ToolDispatch.ReadByWorkspaceIdWithEvictionRetryAsync`, `:54`). A restore writes `obj/`, and a reload needs the load gate and the writer lock, which `IWorkspaceExecutionGate.cs:34` and `:43` forbid re-entering from a read action.
- [ ] The conversion happens at the tool boundary (`CompileCheckTools.cs`). `CompileCheckService` keeps returning its `readiness=restore-required` DTO to its internal consumers (`WorkspaceValidationService`, `CompilationVerification`, `SuppressionService`, `EditService`, `ApplyUndoWorkflowService`), and `CompileCheckServiceTests.cs:148-156` keeps pinning that service shape. The `compile_check` remarks (`CompileCheckTools.cs:25`) describe the error instead of `readiness=restore-required`.
- [ ] `workspace_load` and `workspace_reload` auto-restore by default when any project's assets file is missing, which is unambiguous. Drift-only restore stays opt-in through `autoRestore`. The missing-assets test finds the assets file through the project's own intermediate-output layout. `RestoreStalenessDetector.cs:395` hard-codes the `obj` directory, which misreads `UseArtifactsOutput` projects and would make them restore on every load.
- [ ] The default auto-restore has its own bounded budget, and that budget ends inside the load's remaining request budget with a reserve for the reload. It can therefore never push the call past the gate's `RequestTimeout`. Today the load gate (`WorkspaceTools.cs:53`, `:73`; reload `:117-121`) shares one 2-minute budget across path validation, load, restore, reload and prewarm, and `commandRunner.RunAsync` (`:668-672`) gets only the gate's token. The budget is configuration: a documented default with an env override bound in `Program.cs` beside the other `ROSLYNMCP_*` timeouts, not a literal.
- [ ] The default and the explicit restore take the per-workspace command gate that build and test commands use (`GatedCommandExecutor.cs:73-84`), so a restore never overlaps a `dotnet build` or `dotnet test` on the same workspace. `workspace_load` holds no per-workspace lock, so without the gate a repeat load of an already-loaded path can restore while a build writes the same `obj/`, and a default-on restore makes that likelier. Time spent waiting for the gate counts against the restore budget. `gated-build-test-operation-deadline` states the same requirement for the reload path; whichever row lands first routes the shared restore helper through the gate. Fake-runner test: a load whose restore is triggered while a command holds the workspace's gate waits for it and never runs concurrently.
- [ ] A default auto-restore that fails, times out on its own budget or is otherwise cut short leaves the load successful, with `restoreRequired: true` and a public, path-free reason. Only an explicit `autoRestore=true` fails the call, and a caller cancellation still propagates as cancellation. Red-first fake-runner tests: (a) a restore that outlives its budget yields a successful load, never a `Timeout` error (the gate reclassifies an expired request budget as `TimeoutException`, `WorkspaceExecutionGate.cs:138-150`); (b) a failing restore yields a successful load with the public reason. Both are red today because the default path never runs a restore. Today both explicit-restore failures throw plain `InvalidOperationException` (`WorkspaceTools.cs:664`, `:676`), which the redaction layer collapses to generic text.
- [ ] Server-side restore of a project that already has an assets file targets the package folder recorded there (`project.restore.packagesPath`), passed as a restore argument, instead of whatever the server process environment resolves. It therefore never rewrites a worktree's assets away from its scratch cache. A fake-command-runner test asserts the arguments.
- [ ] When a load leaves `restoreRequired: true`, both the lean and the verbose load result state the required next call at top level. The missing-assets case no longer says that restore inputs changed (`WorkspaceStatusSummaryDto.cs:163`).
- [ ] The contract changes ship with an ADR in `docs/decisions/` (next free number; 0011 is reserved by `v5-major-release-contract-prereqs`), `docs/product-contract.md` entries, and a CHANGELOG migration note (contract care). The changes are the new error category and `compile_check` error, and the default auto-restore with its budget knob. The knob is also documented in `ai_docs/references/environment-variables.md`.

## Evidence

- `CompileCheckService.cs:57-66` short-circuits with a normal result: `Success=false`, zero counts, `Readiness="restore-required"` and a `RestoreHint`. Introduced by `554c1f85` (#1514), which replaced the earlier phantom CS errors.
- At `02db6c49`, `WorkspaceTools.cs:46` defaults `autoRestore = false`, and `:668-672` runs `dotnet restore <path> --nologo` through `IDotnetCommandRunner` directly, with the server's own environment and the load gate's token, outside `GatedCommandExecutor`.
- `RestoreStalenessDetector.IsRestoreRequired` (`:218-268`) returns one bool for "assets file missing" and "assets drifted" alike, so the two cannot be handled differently today.
- Retro 2026-09-27: 55 restore-hint hits across 16 sessions; every fresh `/backlog-remediate` worktree hit it. Agents spent 1-2 extra calls or dropped Roslyn validation for the CLI, and several cold reviewers passed PRs on the executor's word.

## Context

- Related Low rows with different mechanisms, not folded in: `workspace-reload-restore-failure-not-flagged` (restoreRequired detection after NU1201) and `workspace-status-verbose-not-superset`.
- The auto-restore failure sites are also instances of `tool-refusal-public-message-guard`'s mechanism; this row fixes them directly.
- `gated-build-test-operation-deadline` frees build, test, coverage and scan commands from `RequestTimeout`. The load gate is outside its scope, so this row bounds the restore inside the load's own budget instead. A cold restore that needs longer than that budget is left to the explicit `autoRestore=true` call or the CLI. Both rows require the restore helper to take the per-workspace command gate.

## Notes

- Size L (7 production files). Split seam: (1) the `compile_check` `RestoreRequired` error, covering `CompileCheckTools.cs`, `ToolErrorHandler.cs`, `ResourceReadResultFilter.cs` and the ADR entry, is independently shippable and clears the zero-diagnostics trap; (2) the default auto-restore on missing assets with its budget, the command gate and the next-call field, covering `WorkspaceTools.cs`, `RestoreStalenessDetector.cs`, `WorkspaceStatusSummaryDto.cs` and `Program.cs`; (3) the `packagesPath` restore argument (`WorkspaceTools.cs` plus the assets reader) can ride with (2) or follow it.
- The budget cap needs the load's remaining request budget. Either the gate exposes the call's deadline to the action (for example through `AmbientGateMetrics`) or the configured budget is validated against `RequestTimeout` minus a reload reserve. Pick one in the plan and test the cap on a fake clock.
