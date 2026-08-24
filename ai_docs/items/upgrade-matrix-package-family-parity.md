# upgrade-matrix-package-family-parity — Verify every documented dependency pin

**row:** `upgrade-matrix-package-family-parity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `Directory.Packages.props`
- `docs/upgrade-matrix.md`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Correct the Roslyn family and every other stale `Current` value in the upgrade matrix from central package pins.
- [ ] Replace the MCP/Test-SDK-only parity subset with a parser that verifies every package row carrying a central-pin source.
- [ ] Fail on missing, duplicate, malformed, or stale package rows rather than silently skipping an unrecognized family.
- [ ] Add one regression that drifts a non-MCP package version and proves parity verification fails with the package name and both versions.

## Evidence

PR #1326 review found `docs/upgrade-matrix.md` documents the Roslyn API family at 5.6.0 while `Directory.Packages.props` pins 5.9.0. The current parity test intentionally checks only ModelContextProtocol and Microsoft.NET.Test.Sdk, so the broader machine-readable table can remain stale while CI passes.
