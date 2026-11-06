# pwsh-broken-pipe-burst-under-extreme-load — Broken-pipe probe still produced timeouts and a 14 s IOException in one extreme-load round

**row:** `pwsh-broken-pipe-burst-under-extreme-load` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`

## Acceptance

- [ ] The burst is reproduced with a bounded harness and attributed (dotnet host cold start, drain, or scheduler), or retired with counts; any fix keeps the deadline unchanged.

## Evidence

- PR 1999 implementer: one round (40 burners, 16 concurrent) after the fix; not recurred in 288/288 at 24 concurrent.
