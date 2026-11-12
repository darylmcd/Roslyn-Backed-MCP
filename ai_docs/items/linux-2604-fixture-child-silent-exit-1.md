# linux-2604-fixture-child-silent-exit-1 — Root-cause intermittent silent exit-1 of fixture dotnet restore/msbuild children on ubuntu-26.04 hosted legs

**row:** `linux-2604-fixture-child-silent-exit-1` · **pri:** `High` · **size:** `M` · **verified:** `0a2ea88bd2d699f5c1d3874fc0ff4388f0eabea7`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs:624` — RestoreFixture.RestoreInitialAsync asserts the child `dotnet restore` succeeded.
- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs:356` — `dotnet msbuild -m:2` child assertion; same shape at :386.
- `tests/RoslynMcp.Tests/Helpers/CommandExecutionFailureMessage.cs` — failure formatter added by PR 2083.

## Acceptance

- [ ] Reproduce on a hosted ubuntu-26.04 leg (or prove the cause from captured data) why the child exits 1 with empty stderr: determine whether a restore/msbuild node is killed, a shared NuGet/MSBuild state collision between parallel test classes, an SDK 10.0.404 / MSBuild 18.9 behavior, or a 26.04 kernel/runtime difference.
- [ ] Fix the cause (test isolation or product), verified by N consecutive green hosted runs on every linux leg, not by retry.
- [ ] Remove any diagnostic scaffolding that is no longer needed.

## Evidence

- Observed only on ubuntu-26.04 (Image Version 20261101.186.1; last green run 42662924295 was ubuntu-24.04). `ubuntu-latest` rolled between 2026-11-09 and 2026-11-12.
- `AutoRestore_FrameworkConditionedClosure_RetainsFullOuterAssets` failed on runs 43286183707 (PR 2082 head), 43299308538 (PR 2083 workflow-only head, no src change: base-equivalent evidence) and 43325305695 (PR 2082 head, with diagnostics): `dotnet restore .../A/A.csproj --nologo --packages <dir> --no-dependencies` exitCode 1, durationMs 7215, earlyKillReason none, stdout only `Determining projects to restore...`, stderr empty.
- 11 `ForkNativeAdmissionTests` failed in run 43304676563 linux-2 (`Build FAILED. 0 Warning(s) 0 Error(s)`, `FileNotFoundException` on `evidence/context-Child.txt`).
- Host at failure (run 43325305695 linux-4): 15700 MB total, 14692 MB available, no memory pressure, nofile 65536, inotify instances 1280, kernel 7.0.0-1016-aws, SDK 10.0.404.
- Not reproduced in `mcr.microsoft.com/dotnet/sdk:10.0.404-resolute` on Docker Desktop (WSL2) running the same filtered tests; inotify exhaustion by a non-root user did not break `dotnet restore`/`msbuild -m:2`.

## Context

- Blocks the required `validate` check intermittently on any PR. Not caused by PR 2082 (undo changes).
- Pinning the Linux legs to ubuntu-24.04 was rejected by the operator in favor of fixing the cause.
