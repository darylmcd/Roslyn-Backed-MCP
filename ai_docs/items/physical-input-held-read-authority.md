# physical-input-held-read-authority — Bind protected content reads to validated opened objects

**row:** `physical-input-held-read-authority` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`
- `src/RoslynMcp.Roslyn/Services/FileSnapshotCapture.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryScope.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `tests/RoslynMcp.Tests/PhysicalInputCaptureBatchIntegrationTests.cs`
- `tests/RoslynMcp.Tests/FileSnapshotCaptureTests.cs`

## Acceptance

- [ ] Acquire a physical input object and verify the held handle's canonical path and kind before reading content; hash/read through that same held object.
- [ ] Preserve caller policy authorization before acquisition; do not infer grants from the handle or path.
- [ ] Preserve exact canonical Windows volume/drive identity, including refusal of aliases, alternate drives and SUBST spellings currently refused by physical admission.
- [ ] Preserve strict Unix input routes; do not inherit owned-scratch temporary-root alias exemptions.
- [ ] Preserve directory versus missing versus unreadable classification, empty-file bytes, cancellation, shares and cleanup; no unchecked fallback to pathname reads.
- [ ] Migrate both PhysicalInputIdentity hashing and protected byte readers/copies with the same precheck/open gap; do not fix hashing alone.
- [ ] Prove controlled replacement between precheck/acquisition and first content read refuses before any unapproved bytes are read; retain existing authority and original deadline tests.
- [ ] Validate Windows and supported Unix implementations; unsupported handle validation must fail closed, not silently use the old race.

## Evidence

- Original mechanism: pathname admission, independent attributes query, then pathname stream open. A changed parent or leaf could redirect bytes before later hash refusal.
- Installed shared PhysicalInputReader validates the actual held canonical coordinate and regular-file/directory kind before issuing its owned read lease; protected byte consumers and copy sources use the same held stream. Current caller grants remain separate and mandatory.
- Device namespaces refuse before native open. Windows share modes remain Read for required bytes/copies and ReadWrite|Delete for identity capture. Unix lease retains advisory sharing and unlock-before-close.
- Session 2026-10-17: held-reader50 Windows subset passed 61, skipped 3 (Unix-only cases and unavailable file symlink); exact same compiled assemblies on Ubuntu/.NET10.0.400 passed 47, skipped 11 Windows-only cases. Linux exercised both leaf and parent link substitutions. macOS runtime was not exercised.
- Causal held-coordinate-check removal: Windows 2 expected assertion failures plus 1 unavailable-link skip; Linux all 3 expected assertion failures. Byte-identical leaf/parent aliases and moved opened object incorrectly issued readable streams without the guard. Guard restored byte-for-byte before subsequent acceptance run.
- Initial Linux run exposed incorrect async-mode wrapping of raw Unix handles; fixed stream construction to use handle.IsAsync, then generated the passing Windows/Linux evidence above.
- Combined acceptance and shipping remain pending; no deadline or throughput claim from these focused controls.

## Context

- Shared primitive candidates: PhysicalPathResolver GUID-handle canonicalization; PhysicalDirectoryScope descriptor-relative opens and statx/fstat; PhysicalDirectoryLease held route/file identity; CompositeApplyOrchestrator handle path facilities; OwnedShadowImage held-object read view.
- No PinnedFileIdentity symbol exists in the current worktree; do not invent reuse of an absent API.
- Additional protected FileSnapshotCapture consumers include BuildInputProvenance, ForkGraphPreparation, RestoredCompilerInputAuthority, SolutionDiffReadContext, WorkspaceManager.Recovery, UndoService and mutation snapshot services.
- Audit direct physical reads in PhysicalGraphStage, ForkGraphPreparation and TestRunnerService.NativeProtocols, plus physical copy acquisition. Retain trusted native MSBuild/Roslyn evaluation boundaries and caller-supplied semantic callbacks as distinct contracts.
- Inventory and design: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/held-input-read-proposal/design.md`.
