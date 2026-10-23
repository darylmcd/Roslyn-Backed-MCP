# test-probe-custom-output-paths — Copy current referenced probe outputs

**row:** `test-probe-custom-output-paths` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj:49`: CopyGitFixtureWorkerProbe.
- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj:56`: CopyProcessPipeWorkerProbe.
- `tests/GitFixtureWorkerProbe/GitFixtureWorkerProbe.csproj`.
- `tests/ProcessPipeWorkerProbe/ProcessPipeWorkerProbe.csproj`.

## Acceptance

- [ ] Derive probe copy inputs from the referenced projects' actual target outputs.
- [ ] A fresh custom OutDir build copies the just-built probe binaries and runtime companions.
- [ ] A control with distinguishable stale normal-bin payloads fails before repair and passes afterward.
- [ ] Preserve the ordinary output build and process-probe execution contracts.

## Evidence

- Source review during isolated causal-test preparation: both copy targets read fixed bin/Configuration/TargetFramework paths while destination uses OutDir.
- SDK10 custom OutDir redirects referenced project TargetPath; fixed copy sources can select stale normal-bin probes.
- Current narrow report/path causal controls do not consume these probe executables; isolated output is not universally qualified.
