# plugin-package-allowlist-process-test-hygiene — Harden plugin-package process tests

**row:** `plugin-package-allowlist-process-test-hygiene` · **pri:** `Low` · **size:** `S` · **deps:** `powershell-script-test-runner-foundation`

## Anchors

- `tests/RoslynMcp.Tests/PluginPackageAllowlistTests.cs`

## Acceptance

- [ ] Put temporary allowlist/candidate files beneath `TestTempRoot.Current` and clean them through `TestFixtureFileSystem` ownership.
- [ ] Drain redirected output asynchronously, bound PowerShell execution, and kill the process tree on timeout.
- [ ] Mark the shelling tests with the `Process` category and remove the dead platform branch whose arms both return `pwsh`.
- [ ] Keep the canonical-pass and both rejection cases green through one shared bounded runner.

## Evidence

- The adjacent verifier suite writes directly under the shared OS temp root, waits without a timeout, omits the Process lane, and carries a resolver branch with identical results.
