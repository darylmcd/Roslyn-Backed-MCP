# test-assembly-donotparallelize-audit — Re-audit the serialization opt-outs

**row:** `test-assembly-donotparallelize-audit` · **pri:** `Medium` · **size:** `L` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/AssemblyInfo.cs`
- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs`

## Acceptance

- [ ] Each `[DoNotParallelize]` either cites a documented process-global or shared-workspace dependency, or is removed.
- [ ] `AssemblyInfo.cs`'s comment describes the CURRENT ownership model rather than the retired mutable statics.
- [ ] The serialized tail is measured before and after, from repeated TRX evidence rather than a single run.

## Evidence

Counted on `main` 2026-09-03: **122** `[DoNotParallelize]` across **358** `[TestClass]` (~34%).
`AssemblyInfo.cs` declares `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]`
and its own comment names the reason for the opt-outs: *"Classes that share mutable state through
TestBase's static services"*.

PR #1431 replaced exactly those mutable statics with get-only forwarders over one assembly-owned
`TestAssemblyFixture`, so that stated rationale is now partly stale. Whether each opt-out is still
required is an open question, and ~34% of the suite running serially is the structural cause of the
serialized tail (`CI_POLICY.md` records a 10m17s pre-refactor tail).

## Context

This is the follow-on the shipped row anticipated in its "Residual accepted by design" clause: the
forwarder refactor deliberately did not touch parallelization.

**Sized L on purpose — must be split before it is selectable.** A sane decomposition is by opt-out
cause: classes that only READ shared services (likely safe to unmark), classes holding a real
process-global (fixed port, %TEMP%, MSBuild), and classes whose dependency is now expressible through
the fixture. Start with the read-only group, which is the largest and lowest risk.

Do NOT unmark in bulk on one green run — the 2026-09-02 sweep demonstrated that concurrency on this
machine produces spurious failures, so removals need repeated evidence.

[source: 2026-09-03 backlog-remediate PR #1431]
