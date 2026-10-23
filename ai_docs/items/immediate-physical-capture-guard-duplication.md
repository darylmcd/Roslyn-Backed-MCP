# immediate-physical-capture-guard-duplication — Remove duplicate synchronous path resolutions before guarded capture

**row:** `immediate-physical-capture-guard-duplication` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs`
- `src/RoslynMcp.Roslyn/Services/RestoredCompilerInputAuthority.cs`
- `tests/RoslynMcp.Tests/InstalledPackageAuthorityTests.cs`
- `tests/RoslynMcp.Tests/RestoredCompilerInputAuthorityTests.cs`

## Acceptance

- [ ] Remove five duplicate caller resolutions in InstalledPackageAuthority: CaptureMissingAssetsAsync, AssertPackageFilesCurrentAsync, AssertCurrentAsync assets loop, and both CreateAsync asset checks.
- [ ] Remove the duplicate caller resolution before consumed-package CaptureAsync in RestoredCompilerInputAuthority.
- [ ] Preserve every preceding policy callback and the exact current-path guard inside PhysicalInputIdentity.CaptureAsync before GetAttributes/open/hash and before its first asynchronous suspension.
- [ ] Preserve fresh bytes, typed physical identity comparisons, cancellation, path refusal, and all checks across I/O, callbacks or native evaluation boundaries.
- [ ] Verify physical path replacement, package/assets mutation, revoked grant and restored-input authority regressions.
- [ ] Measure acceptance under original deadlines; make no speedup or deadline-recovery claim without current evidence.

## Evidence

| Site | Between removed guard and retained guard |
|---|---|
| InstalledPackageAuthority.CaptureMissingAssetsAsync | Missing identity construction; singleton collection/enumeration. |
| InstalledPackageAuthority.AssertPackageFilesCurrentAsync | Singleton collection/enumeration. |
| InstalledPackageAuthority.AssertCurrentAsync assets loop | Singleton collection/enumeration. |
| InstalledPackageAuthority.CreateAsync initial | SHA256 of supplied approvedAssets memory; singleton collection/enumeration. |
| InstalledPackageAuthority.CreateAsync final | Singleton collection/enumeration. |
| RestoredCompilerInputAuthority consumedPackages loop | Direct call to CaptureAsync. |

- Complete direct-call search covered production EnsurePhysicalPath and PhysicalInputIdentity.CaptureAsync/AssertCurrentAsync references.
- No filesystem I/O or asynchronous boundary occurs between each removed check and the retained capture guard.
- An already-cancelled token reaches CaptureAsync cancellation before redundant caller filesystem resolution; cancellation still refuses the operation.
- Draft contains six deletions across two files; verification and performance measurements remain pending.

## Context

- Separate mechanism from repeated graph evaluation, native condition closure and pooled hashing.
- Keep AuthorizeAsync guards used before arbitrary reads, CompilerArgumentInputContext metadata guards, and certificate proofs bracketing native evaluation.
- Scratch evidence: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/duplicate-physical-guard-draft/`.
- Final preserved guard: `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`, PhysicalInputIdentity.CaptureAsync.
- Status: proposed, scratch only, unverified. No performance claim.
