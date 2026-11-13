# test-pid-file-read-before-content-published — Audit remaining tests that read a child PID file after only checking it exists

**row:** `test-pid-file-read-before-content-published` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs:1113` — `int.Parse(File.ReadAllText(pidPath).Trim())` after a gate on other files; confirm pidPath is published before the gate.
- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs:132` — parses `formatter.pid` after the probe exits; likely safe, confirm.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs:298` — child.pid fixtures; confirm each wait reads content.

## Acceptance

- [ ] Each site either waits for non-empty content or is shown to read only after the writer exited.

## Evidence

- PR 2087 fixed the same mechanism at `ForkNativeAdmissionTests.cs:1370` (`FormatException: The input string '' was not in a correct format`) and `tests/ProcessPipeWorkerProbe/Program.cs` `WaitForFile`.

## Context

- Same defect mechanism as the 2087 fix; sites above were not proven vulnerable.
