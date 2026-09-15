# changed-format-test-process-runner-lifetime — changed-format-test-process-runner-lifetime

**row:** `changed-format-test-process-runner-lifetime` · **pri:** `Low` · **size:** `S`

# changed-format-test-process-runner-lifetime

## Anchors

- `tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs` — RunProcess timeout branch.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs` — existing RunExecutableAsync ownership helper.

## Acceptance

- Replace duplicated process and stream ownership with the shared runner, preserving existing formatter scenarios and diagnostic assertions.
- Bound process exit and stdout/stderr drain after timeout before fixture deletion.
- Add one controlled timeout regression that proves the child exits and readers complete before the fixture is removed.

## Evidence

2026-09-15 direct review: RunProcess calls Kill(entireProcessTree: true) and immediately throws; it does not await child exit or observe its pending stdout/stderr tasks before disposing the process and cleaning the fixture.
