# pwsh-script-runner-drain-waits-full-budget — RequestTerminationAndDrainAsync waits the whole drain when a descendant survives the kill

**row:** `pwsh-script-runner-drain-waits-full-budget` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs`

## Acceptance

- [ ] Cleanup returns promptly once the process tree is dead or the descendant is identified; a deterministic test with a surviving descendant is red first.

## Evidence

- PR 1999: with a sleep grandchild, 3-11 of 1440 loaded docker runs took 10.2-10.5 s to return; the drain cause is inferred.
