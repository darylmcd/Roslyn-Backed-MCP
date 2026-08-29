# test-run-nested-git-child-hang — Prevent nested Git hangs under test_run

**row:** `test-run-nested-git-child-hang` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Core/Services/DotnetCommandRunner.cs`
- `tests/RoslynMcp.Tests/TestRunnerServiceTests.cs`

## Acceptance

- [ ] Reproduce the difference between raw `dotnet test` and MCP `test_run` when a test launches PowerShell and that script runs `git ls-files` in the loaded repository.
- [ ] Remove the inherited environment, process-lifecycle, or execution-gate cause so the nested Git child completes under `test_run` without weakening path or process isolation.
- [ ] Preserve bounded timeout and process-tree cleanup; report secret-safe child-stage diagnostics when the nested command does not exit.
- [ ] One regression proves the same nested Git fixture completes through `test_run` and raw `dotnet test`.

## Evidence

On 2026-08-29, `PluginPackageAllowlistTests.VerifyPluginPackageFiles_PassesCanonicalAllowlist` passed through raw `dotnet test` in 464 ms, but timed out twice through MCP `test_run` at 30 and 60 seconds. Live process inspection during the first timeout showed the PowerShell verifier blocked in its child `git -C C:/Code-Repo/Roslyn-Backed-MCP ls-files`; the shared runner terminated the tree when its timeout fired. The same script completed directly in 446 ms. Treat the cause as unconfirmed until the process environment and lifecycle are compared.
