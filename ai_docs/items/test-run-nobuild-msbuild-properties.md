# test-run-nobuild-msbuild-properties — test_run cannot skip the build or pass MSBuild properties

**row:** `test-run-nobuild-msbuild-properties` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:170-373`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:331-393`
- `src/RoslynMcp.Core/Services/ITestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs:240-256`
- (new) `tests/RoslynMcp.Tests/TestRunBuildOptionsTests.cs`

## Acceptance

- [ ] `test_run` accepts `noBuild`, which adds `--no-build` to both argument shapes (VSTest `BuildVsTestArguments` and MTP-native `BuildMtpNativeArguments`). The parameter description states that a no-build run executes the last built binaries.
- [ ] `test_run` accepts `msbuildProperties` (a name/value map), passed as `-p:Name=Value` with each value a separate process argument, never through a shell string. Verify both argument shapes: native MTP mode forwards arguments it does not recognise to the test host (`TestRunnerService.cs:355-358`).
- [ ] Property names outside an allowlist are refused with a coded, public error that names the allowlist. The allowlist is configuration (a `ValidationServiceOptions` entry with a documented default and an env override bound in `Program.cs`), not a literal in the service.
- [ ] Tests assert the exact argument list for each combination and the refusal for a non-allowlisted property.
- [ ] The new options reach `TestRunnerService` through a default-implemented `ITestRunnerService` overload, so the 9 test doubles (in 6 test files, re-counted at `02db6c49`) that implement the interface need no change.

## Evidence

- `test_run` parameters at `19ccd61b` are `workspaceId`, `projectName`, `filter`, `failuresOffset`, `failuresLimit` and `compact` only. There is no separate build step: `dotnet test` builds implicitly, from the VSTest arguments at `TestRunnerService.cs:331-350` or the MTP-native arguments at `:378-393`, executed at `:148`. The optional restore at `:314` runs only for TUnit OR filters.
- Retro 2026-09-27: agents ran `dotnet test --no-build -p:SkipFrontendBuild=true` through Bash instead, because repos with a frontend build step rebuild on every MCP call.

## Context

- Medium, not High: the CLI workaround is sound, and the 120 s cancellation itself is `gated-build-test-operation-deadline`. Raise it if MCP `per_edit_test` adoption in multi-worktree sweeps is the goal.
- No dependency on `gated-build-test-operation-deadline`: neither half needs it, and `noBuild` alone shortens the runs that hit the cap (the retro's CLI `--no-build` runs take about a minute). Both rows edit `ValidationTools.cs` and `TestRunnerService.cs`; the planner's hotspot ordering sequences them.

## Notes

- Size L: two independently shippable halves. Split seam: (1) `noBuild` touches `ValidationTools.cs`, `TestRunnerService.cs` and `ITestRunnerService.cs`; (2) the `msbuildProperties` allowlist adds `ValidationServiceOptions.cs` and `Program.cs`. With `--no-build`, the retro's `-p:SkipFrontendBuild=true` is likely redundant (it gates a build step), so (1) alone probably clears the retro evidence.
