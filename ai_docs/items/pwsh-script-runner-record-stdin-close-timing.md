# pwsh-script-runner-record-stdin-close-timing — Runner diagnostics do not record when the child closed stdin

**row:** `pwsh-script-runner-record-stdin-close-timing` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`

## Acceptance

- [ ] The timeout and failure messages include the elapsed time to first stdin close or its absence; a test pins the field.

## Evidence

- PR 1999: Linux run 41911201587 write-phase timeout was not reproducible in about 6,000 loaded docker runs and left no close-timing evidence.
