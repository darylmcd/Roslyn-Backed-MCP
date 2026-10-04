# workspace-close-global-build-server-shutdown - Scope explicit workspace drain to owned servers

**row:** `workspace-close-global-build-server-shutdown` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (`WorkspaceClose` drain path)
- `src/RoslynMcp.Host.Stdio/Tools/DetachedTestHostDrain.cs`
- `src/RoslynMcp.Core/Services/IDotnetCommandRunner.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs`
- `tests/RoslynMcp.Tests/PromptSmokeTests.cs`

## Acceptance

- [ ] Replace explicit `workspace_close(drainProcesses:true)` machine-wide build-server shutdown with verified workspace-owned process/resource cleanup.
- [ ] Preserve an unrelated workspace's live worker during drain; cover unavailable ownership and PID reuse through existing drain seams.
- [ ] Update tool descriptions, refactoring prompt, and corresponding contract tests; document any contract change under repository compatibility rules.

## Evidence

- Re-derived 2026-10-04 on `afa0ecea`: the opt-in workspace lifecycle path still calls the runner with `["build-server", "shutdown"]`; its working directory does not scope those servers.
- This is an explicit public workspace-lifecycle contract, separate from automatic validation fixture/verifier cleanup. The validation-isolation task removes automatic broad shutdowns without redefining this opt-in surface.
