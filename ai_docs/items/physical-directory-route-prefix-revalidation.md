# physical-directory-route-prefix-revalidation — Acquire each held Windows route prefix once

**row:** `physical-directory-route-prefix-revalidation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryLease.cs`: OpenRouteWindowsCore, OpenWindowsCore, OpenNativeChild.
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryScope.cs`: WindowsDirectory, AssertCurrentPath.
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs`: TryGetCanonicalWindowsHandlePath.
- `src/RoslynMcp.Roslyn/Helpers/PhysicalInputReader.cs`: ValidateRequest, ReadFileLastWriteTimeUtcIfPresent.
- `tests/RoslynMcp.Tests/Workspace/PhysicalGraphCleanupAuthorityTests.cs`: ForkFailureCleanup_PreservesOrdinaryReplacement.
- `tests/RoslynMcp.Tests/PhysicalDirectoryAcquisitionAuthorityTests.cs`.
- `tests/RoslynMcp.Tests/WindowsDirectoryCoordinateAuthorityTests.cs`.

## Acceptance

- [ ] Acquire each actual Windows route component through held no-follow parent handles; preserve effective no-delete pins for every prefix, missing-path outcomes and reverse cleanup.
- [ ] Reuse the existing native relative-open implementation; no duplicate P/Invoke, global cache, grant bypass, removed kind proof or extended timeout.
- [ ] Preserve original Win32 selection and compare exact held identities before publishing a route.
- [ ] Preserve explicit literal dot/space components through canonical route slicing, UNC handle coordinates, reader comparison and metadata IO.
- [ ] Use original request normalization before extended-prefix removal; retain raw component-order link admission and native refusal of unsupported extended navigation.
- [ ] Differential controls cover ordinary/extended bytes, object identities, timestamps, intermediate parent pins and missing paths.
- [ ] Preserve root/ancestor replacement, alias and file-vs-directory controls; public one-project fork cleanup reaches its intended restore callback within its existing request budget.
- [ ] Keep current input grants, after-await checks and final physical proofs unchanged; qualify fresh Windows and Linux controls before full gate and merge.

## Evidence

- 2026-10-23 restore-compiled-positive3.log: cleanup normal/root and actual fork authority tests time out at the unchanged 120-second workspace request limit before the restore callback. No base/environment/flake disposition.
- Owned testhost 95824 was traced through parent 240760 to admitted command 309856. Read-only dotnet-stack report compiled-positive3-stacks1.log sampled PhysicalPathResolver.ResolveCore -> OpenWindowsCore -> OpenRouteWindowsCore -> AssertCurrentPath -> parser kind proof.
- Each route prefix currently invokes full-path physical resolution twice, so one complete route repeatedly walks its prefixes. The sample establishes the repeated mechanism, not its complete share of the request deadline; fresh runtime must establish the repair.
- Fix is required in the current restore PR; this row does not authorize deferral or a lesser change.
