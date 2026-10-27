# test-probe-custom-output-paths — Copy the current referenced process-probe outputs

**row:** `test-probe-custom-output-paths` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj:49`
- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj:56`

## Acceptance

- [ ] Derive copy inputs from referenced projects actual output targets, including runtime companions.
- [ ] Prove fresh custom OutDir copies new probes rather than distinguishable stale normal-bin files; preserve ordinary output/probe execution.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Both Copy targets glob fixed ../Probe/bin/Configuration/TargetFramework while destination uses OutDir. Current main retains these targets.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
