# process-pipe-worker-probe-private-reflection — Give the probe an explicit seam

**row:** `process-pipe-worker-probe-private-reflection` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/ProcessPipeWorkerProbe/Program.cs:68`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`

## Acceptance

- [ ] Replace private-method reflection and its positional argument-count dependency with a stable explicit probe API.
- [ ] Preserve the worker probe scenarios and assert they survive an internal runner signature change.

## Evidence

- During PR #1971, adding an optional input parameter broke both probe cases with `TargetParameterCountException`; the final PR restored the exact old signature, leaving the brittle reflection seam at `Program.cs:68,76`.
