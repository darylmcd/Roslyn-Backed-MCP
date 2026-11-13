# native-late-consumer-marker-intermittent-missing-output — Root-cause the intermittent missing marker output in NativeLateConsumers_CopyMarkerAndManifests on ubuntu-26.04

**row:** `native-late-consumer-marker-intermittent-missing-output` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs:293` — `Assert.IsTrue(File.Exists(output))` carries no `Describe()` and the command ran at minimal verbosity, so the failure is undiagnosable.

## Acceptance

- [ ] The failing asserts report the command, exit code, stdout and stderr (`CommandExecutionFailureMessage.Describe`).
- [ ] The cause is identified from captured data (SDK 11.0.100 vs 10.0.404 `_CopyFilesMarkedCopyLocal` behavior, timestamp/hardlink skip, or a test-ordering effect) and fixed, verified by N consecutive green hosted linux-2 runs, not by retry.

## Evidence

- Run 43371278172 (PR 2082 head, after the 2087 SDK-env fix): linux-2 failed `("marker",False,True)`, `("marker",True,False)`, `("marker-prior-flags",False,True)`, `("marker-prior-flags",True,False)` within 16 s; each `result.Succeeded` was true and `File.Exists(output)` false. Re-run of the failed leg passed. The same variants passed on runs 43365038953 and 43360988929.
- Not proven on the base commit: PR 2082 touches no MSBuild or fork-admission code.

## Context

- Fixture children run `dotnet msbuild` from a temp dir with no global.json, so on ubuntu-26.04 they resolve SDK 11.0.100 (MSBuild 18.12) while the repo pins 10.0.404.
