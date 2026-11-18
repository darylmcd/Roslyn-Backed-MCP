# git-script-windows-pipe-worker-capacity — Qualify Windows worker capacity for redirected Git and script streams

**row:** `git-script-windows-pipe-worker-capacity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GitChangedFilesCollector.cs:110-121`
- `src/RoslynMcp.Roslyn/Services/ScriptWorkerProcess.cs:66-69`
- `tests/RoslynMcp.Tests/Helpers/ProcessPipeReader.cs:65-92`
- `tests/ProcessPipeWorkerProbe/Program.cs`

## Acceptance

- [ ] Use isolated Windows worker capacity and silent owned writers to qualify sentinel progress in the actual Git and script readers. Leave the parent test host's pool settings alone.
- [ ] Reuse the existing sole-reader native transport if the dependency is confirmed. Preserve deadline, cancellation, exact decoded output and caller-facing error contracts.

## Evidence

- At `c705e2e1`, both owners start async reads on BCL Windows synchronous anonymous pipes. Idle reads can occupy a worker per pipe. Existing isolated probe and ProcessPipeReader cover this transport mechanism for test harness output. Qualify both production routes with the isolated probe.

## Context

- Coordinate any shared native reader with the DotnetCommandRunner drain repair. Keep resolver behavior, stream disposal and capture deadlines explicit for each owner.
