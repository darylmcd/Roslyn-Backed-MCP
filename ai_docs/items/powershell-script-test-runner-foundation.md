# powershell-script-test-runner-foundation — Share PowerShell process-test execution

**row:** `powershell-script-test-runner-foundation` · **pri:** `Low` · **size:** `S`

## Anchors

- New `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`.
- `tests/RoslynMcp.Tests/BreakingVersionGateTests.cs`.
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs`.

## Acceptance

- [ ] Centralize OS-safe `pwsh` resolution, `ArgumentList` construction, concurrent stdout/stderr draining, bounded wait, and process-tree termination.
- [ ] Return one immutable exit/stdout/stderr result and preserve diagnostic output on both success and failure.
- [ ] Migrate the two anchored consumers without weakening their script-specific assertions.
- [ ] One helper contract matrix covers argument boundaries, nonzero exit/output capture, and timeout tree-kill behavior.

## Evidence

- The breaking-version and release-child fixtures duplicate the full PowerShell process harness already present in `PluginPackageAllowlistTests` and several skill-script suites; those copies can drift on quoting, stream draining, and timeout cleanup. The separate plugin-test hygiene row can adopt the foundation after it lands.
