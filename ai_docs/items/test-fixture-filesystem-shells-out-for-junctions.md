# test-fixture-filesystem-shells-out-for-junctions — Fixture helper launches cmd.exe

**row:** `test-fixture-filesystem-shells-out-for-junctions` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs`

## Acceptance

- [ ] Junction creation no longer launches a process from fixture setup, or the shell-out is confined to a path that cannot run during ordinary test setup.
- [ ] If the fallback must stay, its failure surfaces a diagnostic naming the target path instead of a bare timeout.

## Evidence

Verified against `main` on 2026-09-03: `TestFixtureFileSystem` shells out to `cmd /c mklink /J`
(`:87`) as a fallback for `Directory.CreateSymbolicLink`, with a hardcoded `cmd.exe` fallback path
and a bounded wait.

Process launch inside a fixture helper is a flake surface under load. That is not theoretical here:
the 2026-09-02 sweep proved concurrent test suites on this machine produce spurious failures,
including a five-minute formatter-generator timeout attributed to host contention.

## Context

Anchor note: the observation was originally reported at `tests/RoslynMcp.Tests/TestFixtureFileSystem.cs`,
which does not exist — the type lives under `TestInfrastructure/`. Corrected here by direct
verification.

Surfaced by the executor of PR #1431 (Directive #3); pre-existing.

[source: 2026-09-03 backlog-remediate PR #1431]
