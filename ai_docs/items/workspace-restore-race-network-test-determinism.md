# workspace-restore-race-network-test-determinism — Stabilize the network restore-race regression

**row:** `workspace-restore-race-network-test-determinism` · **pri:** `Medium` · **size:** `S`

## Anchors

- `Directory.Packages.props`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs:245`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs:8`

## Acceptance

- [ ] Derive the fixture's original `Microsoft.NET.Test.Sdk` version from the copied `Directory.Packages.props`; do not pin a stale expected original version in the test.
- [ ] Keep the unique temp fixture alive through restore, reload, asset verification, and all asynchronous callbacks; repeated isolated runs do not lose `Directory.Packages.props`.
- [ ] Prove the package edit still creates restore drift and `RestoreAndReloadIfRequiredAsync` clears it, including refreshed assets for the selected alternate version.
- [ ] Run the Network test repeatedly and through the unfiltered weekly validation lane without `FileNotFoundException` or fixture-version drift.

## Evidence

- The 2026-07-16 backlog sweep's unfiltered gate failed on the hardcoded `17.14.0` expectation while current central packages use `17.14.1`; an independent untouched-main rerun failed earlier because the copied `Directory.Packages.props` disappeared before line 260.

## Context

PR CI excludes Network tests by policy, so this defect does not block ordinary pull requests but breaks the unfiltered local/weekly canary. Fix the fixture contract, not the unrelated initiative that exposed it.

## Notes

- Preserve the test's live-restore purpose; do not weaken it into a mocked restore.
- Do not add this test to `ai_docs/known-flakes.md` without separate flake triage.
