# test-run-nobuild-msbuild-properties — Pass allowlisted MSBuild properties to test runs

**row:** `test-run-nobuild-msbuild-properties` · **pri:** `Medium` · **size:** `M` · **deps:** `test-run-no-build`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs`

## Acceptance

- [ ] Opt-in `test_run msbuildProperties` applies exactly one property assignment per permitted key across VSTest and native MTP, including any explicit planning restore. Refuse disallowed names or values before invocation; prevent MSBuild assignment injection as well as shell injection. A disallowed name returns an authored InvalidArgument with safe allowed-name guidance. Document the allowlist default and environment override, keep property values out of public diagnostics and logs, and preserve omitted-option behavior. Prove literal propagation and refusal through the public route and both real runner shapes.

## Evidence

- Base 52f2c418: ValidationTools.cs and TestRunnerService.cs have no caller-provided MSBuild-property path. The original 2026-09-27 retro used CLI `--no-build -p:SkipFrontendBuild=true`.

## Context

- Additive 5.x request option if its complete trust boundary can be implemented without changing existing output fields. Evaluate resulting MSBuild property and restoration context, including native MTP engine checks; preserve existing no-option behavior. Do not retain an interface default solely to avoid updating callers. Sequence after test-run-no-build because both change the same runner planning seam; the options have independently testable behavior.
