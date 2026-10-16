# test-run-worktree-package-isolation — Support isolated package roots for MCP test execution

**row:** `test-run-worktree-package-isolation` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:210-229`
- `src/RoslynMcp.Core/Services/ITestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:165-188`
- `src/RoslynMcp.Core/Services/IGatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `src/RoslynMcp.Core/Services/IDotnetCommandRunner.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:155-179`
- (new) `tests/RoslynMcp.Tests/TestRunPackageIsolationTests.cs`

## Acceptance

- [ ] Provide an explicit, safe per-worktree package-root authority for MCP test subprocess execution; carry it through required execution contracts without mutating the server-global environment or adding compatibility defaults to avoid callers. Re-vet the complete caller and public-contract ripple before implementation.
- [ ] Red-first real child-process tests prove two concurrent workspace runs receive their distinct owned roots and retain configured default behavior; preserve timeout/queue/cancellation ownership, structured results and source project isolation. Do not expose arbitrary environment/secret forwarding.
- [ ] Document the environment boundary and sanctioned scoped-test fallback. Preserve public compatibility policy; any stable breaking change requires ADR/migration/release controls. This row is not repaired merely by documenting the current limitation.

## Evidence

| Snapshot | Evidence | Verification limit |
|---|---|---|
| Main337fc2cde10eca3f591cc016c146dee7b893e26a | ValidationTools.RunTests210-229 exposes workspace/project/filter/paging/compact inputs, no per-call package/environment authority. Runner.CreateStartInfo155-179 inherits the server environment and sets only MSBUILDDISABLENODEREUSE explicitly. Existing runner/executor contracts have no execution-environment input. | Source/schema trace; no cache corruption or cross-workspace runtime failure claimed. |
| Primary executor2026-10-10 | Live test_run performed eight actual causal red tests using server environment; its tool schema has no per-call environment overrides. Remaining owned gates require the setup helper's distinct NUGET_PACKAGES and use structured script-bridge CLI execution. | Capability mismatch with mandatory worktree isolation; causal red proof remains valid, not final isolated gate proof. |

## Context

- Separate from restore-callers-missing-packages-path, which preserves assets-recorded roots in explicit restore planning; this row owns execution-environment authority for MCP-scoped jobs.
- File outside the selected initiative set. Keep MCP semantic reads/compile checks where applicable and keep this feature separate from the active primary-constructor repair.
