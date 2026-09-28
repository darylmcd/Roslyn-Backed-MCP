# compile-check-restore-required-handshake — compile_check answers an unrestored workspace with success:false and zero diagnostics; workspace_load never auto-restores

**row:** `compile-check-restore-required-handshake` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Models/CompileCheckDto.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:50-70`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:20-75`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:218-268`
- `src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs:136-165`
- `src/RoslynMcp.Host.Stdio/Program.cs:240-260`
- (new) `src/RoslynMcp.Core/Models/NextCallDto.cs`
- `docs/decisions/README.md`
- `docs/product-contract.md`
- `ai_docs/references/environment-variables.md`
- (new) `tests/RoslynMcp.Tests/CompileCheckRestoreRequiredWireTests.cs`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadAutoRestoreTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs:140-160`

## Acceptance

- [ ] Compatibility class: minor-compatible (additive) under `docs/release-policy.md:19` and `:27-28`, so the row ships on the 4.x line. It adds no error category, turns no result into `isError`, removes no field and changes no field's meaning. The one existing wire value that can change is `exceptionType` on the explicit-restore failure path, recorded as `Changed` (see the restore-failure bullet). `compile_check` is stable (`ServerSurfaceCatalog.Analysis.cs:46`). Its restore-required result (`success:false`, zero counts, `readiness:"restore-required"`, `restoreHint`; `CompileCheckService.cs:57-66`) is documented in its remarks (`CompileCheckTools.cs:25`) and has shipped since v4.2.0 (`554c1f85`, #1514). None of `compile_check`, `workspace_load` or `workspace_reload` advertises an output schema (`ToolOutputSchemaIndex.cs:221-242`), so new fields are allowed; consumers must ignore unknown fields.
- [ ] `compile_check` keeps its restore-required result unchanged and adds a top-level `nextCall` field that names the one call that fixes it: `{"tool": "workspace_reload", "arguments": {"workspaceId": "<id>", "autoRestore": true}}`. `CompileCheckService` sets it beside `RestoreHint` (`:57-66`), so the service's internal consumers (`WorkspaceValidationService`, `CompilationVerification`, `SuppressionService`, `EditService`, `ApplyUndoWorkflowService`) keep working, and the `compileResult` that `validate_*` embeds carries it too. `CompileCheckServiceTests.cs:148-156` stays green unchanged and gains a `nextCall` assertion. The remarks (`CompileCheckTools.cs:25`) mention the field. Red-first wire test through the tool: the result still has `success:false`, `readiness:"restore-required"` and `restoreHint`, and now has `nextCall`.
- [ ] `compile_check` never restores or reloads itself. It is annotated `ReadOnly=true, Idempotent=true` (`CompileCheckTools.cs:27`) and runs under the per-workspace read lock (`ToolDispatch.ReadByWorkspaceIdWithEvictionRetryAsync`, `:54`). A restore writes `obj/`, and a reload needs the load gate and the writer lock, which `IWorkspaceExecutionGate.cs:34` and `:43` forbid re-entering from a read action.
- [ ] `workspace_load` and `workspace_reload` change `autoRestore` from `bool = false` (`WorkspaceTools.cs:46`, `:111`) to `bool? = null`, the pattern `prewarm` already uses (`:47`, `:704-705`):
  - Omitted: restore only when a project's assets file is missing, which is unambiguous.
  - `true`: today's behavior. Restore whenever `restoreRequired` is true, drift included, and fail the call when the restore fails.
  - `false`: never restore. This is today's omitted behavior and the caller's opt-out.
  - Both parameter descriptions and the `workspace_reload` tool description (`:103`) state all three values. The widened input schema accepts every request that is valid today; the live `prewarm` schema is `"type": ["boolean", "null"]`.
  - Precedent: omitted `prewarm` began auto-warming large solutions as a non-breaking `Changed` entry in 1.34.2 (`CHANGELOG.md:1165`), with `false` as the opt-out.
- [ ] The missing-assets test finds the assets file through the project's own intermediate-output layout. `RestoreStalenessDetector.cs:395` hard-codes the `obj` directory, which misreads `UseArtifactsOutput` projects and would make them restore on every load.
- [ ] The default auto-restore has its own bounded budget, and that budget ends inside the load's remaining request budget with a reserve for the reload. It can therefore never push the call past the gate's `RequestTimeout`. Today the load gate (`WorkspaceTools.cs:53`, `:73`; reload `:117-121`) shares one 2-minute budget across path validation, load, restore, reload and prewarm, and `commandRunner.RunAsync` (`:668-672`) gets only the gate's token. The budget is configuration: a documented default with an env override bound in `Program.cs` beside the other `ROSLYNMCP_*` timeouts, not a literal.
- [ ] The default and the explicit restore take the per-workspace command gate that build and test commands use (`GatedCommandExecutor.cs:73-84`), so a restore never overlaps a `dotnet build` or `dotnet test` on the same workspace. `workspace_load` holds no per-workspace lock, so without the gate a repeat load of an already-loaded path can restore while a build writes the same `obj/`, and a default-on restore makes that likelier. Time spent waiting for the gate counts against the restore budget. `gated-build-test-operation-deadline` states the same requirement for the reload path; whichever row lands first routes the shared restore helper through the gate. Fake-runner test: a load whose restore is triggered while a command holds the workspace's gate waits for it and never runs concurrently.
- [ ] A default auto-restore that fails, times out on its own budget or is otherwise cut short leaves the load successful, with `restoreRequired: true` and a public, path-free reason. Only an explicit `autoRestore=true` fails the call, and a caller cancellation still propagates as cancellation. Red-first fake-runner tests: (a) a restore that outlives its budget yields a successful load, never a `Timeout` error (the gate reclassifies an expired request budget as `TimeoutException`, `WorkspaceExecutionGate.cs:138-150`); (b) a failing restore yields a successful load with the public reason. Both are red today because the omitted-`autoRestore` path never runs a restore. Today both explicit-restore failures throw plain `InvalidOperationException` (`WorkspaceTools.cs:664`, `:676`), which the redaction layer collapses to generic text. They become `PublicInvalidOperationException` with a path-free message and keep category `InvalidOperation`. The envelope writes `exceptionType` as `ex.GetType().Name` (`ToolErrorHandler.cs:670`), so that field changes from `InvalidOperationException` to `PublicInvalidOperationException`, unless `public-argument-exception-core-move` has landed first; its marker normalization renders the BCL name. This High row does not depend on that Medium row, because the flip is a `Changed` entry, not a breaking one; core-move's § Wire compat records the flip back as `Changed` too. Wire test in `WorkspaceLoadAutoRestoreTests`: an explicit `autoRestore=true` whose restore fails returns category `InvalidOperation`, the public reason and `exceptionType` `PublicInvalidOperationException`, the value `TestRunFailureEnvelopeTests.cs:1065` pins for that type today. When core-move lands it re-pins both tests to `InvalidOperationException`. If core-move landed first, pin `InvalidOperationException` and drop the fragment's `exceptionType` clause.
- [ ] Server-side restore of a project that already has an assets file targets the package folder recorded there (`project.restore.packagesPath`), passed as a restore argument, instead of whatever the server process environment resolves. It therefore never rewrites a worktree's assets away from its scratch cache. A fake-command-runner test asserts the arguments.
- [ ] When a load or reload leaves `restoreRequired: true`, both the lean and the verbose result carry the same top-level `nextCall`. `SerializeWorkspaceLoadResult` (`WorkspaceTools.cs:682-702`) appends it as a JSON node, the way it appends `prewarm` (`:700`). It is not a new property on `WorkspaceStatusSummaryDto` or `WorkspaceStatusDto`, which back `workspace_status`'s advertised `oneOf` output schema (`ToolOutputSchemaIndex.cs:240-241`, `WorkspaceTools.cs:409`). The missing-assets case no longer says that restore inputs changed (`WorkspaceStatusSummaryDto.cs:163`), a text change to the existing `restoreHint` value.
- [ ] Contract documents ship with the change (contract care):
  - `docs/product-contract.md` documents the three `autoRestore` values and the `nextCall` field.
  - An ADR in `docs/decisions/` (next free number at implementation time) records the compatibility disposition: why the omitted-`autoRestore` default is minor-compatible, the `false` opt-out, and the rejected breaking alternative under Context.
  - `changelog.d/` fragments classify the changes as `Added` (`nextCall`) and `Changed` (omitted `autoRestore` restores missing assets; `autoRestore=false` opts out; an explicit-restore failure states its reason and, before core-move, reports `exceptionType` `PublicInvalidOperationException`). Neither is `Changed — BREAKING`.
  - The restore budget knob is documented in `ai_docs/references/environment-variables.md`.

## Evidence

- `CompileCheckService.cs:57-66` short-circuits with a normal result: `Success=false`, zero counts, `Readiness="restore-required"` and a `RestoreHint`. Introduced by `554c1f85` (#1514), which replaced the earlier phantom CS errors. `git tag --contains 554c1f85` lists v4.2.0 and v4.2.1.
- At `02db6c49`, `WorkspaceTools.cs:46` defaults `autoRestore = false`, and `:668-672` runs `dotnet restore <path> --nologo` through `IDotnetCommandRunner` directly, with the server's own environment and the load gate's token, outside `GatedCommandExecutor`. A `bool` parameter cannot tell an omitted value from an explicit `false`.
- `RestoreStalenessDetector.IsRestoreRequired` (`:218-268`) returns one bool for "assets file missing" and "assets drifted" alike, so the two cannot be handled differently today.
- Retro 2026-09-27: 55 restore-hint hits across 16 sessions; every fresh `/backlog-remediate` worktree hit it. Agents spent 1-2 extra calls or dropped Roslyn validation for the CLI, and several cold reviewers passed PRs on the executor's word.

## Context

- Rejected alternative: a structured `RestoreRequired` tool error (`isError: true`, new category) in place of the restore-required result. It converts a shipped stable result into an error with a new category, which `docs/release-policy.md:24-26` and `:36` make a major-version change even when the old shape was a bug. The repo classified the same kind of change as `Changed — BREAKING` (`changelog.d/workspace-id-unknown-error-category.md`). With the default restore and `nextCall`, the zero-diagnostics result is rare and machine-actionable, so the conversion is not needed.
- Related Low rows with different mechanisms, not folded in: `workspace-reload-restore-failure-not-flagged` (restoreRequired detection after NU1201) and `workspace-status-verbose-not-superset`.
- The auto-restore failure sites are also instances of `tool-refusal-public-message-guard`'s mechanism; this row fixes them directly.
- `gated-build-test-operation-deadline` frees build, test, coverage and scan commands from `RequestTimeout`. The load gate is outside its scope, so this row bounds the restore inside the load's own budget instead. A cold restore that needs longer than that budget is left to the explicit `autoRestore=true` call or the CLI. Both rows require the restore helper to take the per-workspace command gate.

## Notes

- Size L (8 production files). Split seams:
  - (1) `nextCall` on `compile_check` and on the load and reload results (`CompileCheckDto.cs`, `CompileCheckService.cs`, `CompileCheckTools.cs`, the shared `NextCallDto.cs`, and the serializer in `WorkspaceTools.cs`). It ships on its own and makes the restore-required result machine-actionable.
  - (2) the nullable `autoRestore` default restore with its budget and the command gate (`WorkspaceTools.cs`, `RestoreStalenessDetector.cs`, `WorkspaceStatusSummaryDto.cs`, `Program.cs`), with the ADR and the product-contract entries.
  - (3) the `packagesPath` restore argument (`WorkspaceTools.cs` plus the assets reader) can ride with (2) or follow it.
- The budget cap needs the load's remaining request budget. Either the gate exposes the call's deadline to the action (for example through `AmbientGateMetrics`) or the configured budget is validated against `RequestTimeout` minus a reload reserve. Pick one in the plan and test the cap on a fake clock.
- 2026-09-28: reworked to the additive shape after the operator chose an additive 4.x release (PR #1655 review round 4).
