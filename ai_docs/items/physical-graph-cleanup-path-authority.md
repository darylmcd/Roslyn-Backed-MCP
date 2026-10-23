# physical-graph-cleanup-path-authority — Retain issued directory identity through graph cleanup

**row:** `physical-graph-cleanup-path-authority` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryScope.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryLease.cs`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `tests/RoslynMcp.Tests/PhysicalGraphStageTests.cs`
- `tests/RoslynMcp.Tests/PhysicalDirectoryRetryAuthorityTests.cs`
- `tests/RoslynMcp.Tests/Workspace/PhysicalGraphCleanupAuthorityTests.cs`
- `tests/RoslynMcp.Tests/RestoreBoundaryRegressionTests.cs`

## Acceptance

- [ ] Bind stage/fork cleanup authority to the directory identity issued at creation; never acquire cleanup ownership from its later pathname.
- [ ] Cover stage rebuild, failed creation, successful disposal, fork cleanup and expired-fork traversal through one shared bounded no-follow namespace implementation.
- [ ] Preserve unrelated ordinary replacement directories and replaced ancestors; no symlink is required for the regression.
- [ ] Retain effective Windows route pins and Unix descriptor-relative traversal/deletion; preserve existing normal, readonly, cancellation and link controls.
- [ ] Preserve primary failures, cancellation tokens and bounded sanitized cleanup outcomes.
- [ ] Observe actual old destructive failures and fresh caller-level controls before closing; complete current-head gates and merge.

## Evidence

| Mechanism | Current evidence / status |
|---|---|
| Stage retained authority | Static review: PhysicalGraphStage stores directory spellings, then constructs DirectoryScope only at rebuild/catch/Dispose; DirectoryScope calls a recursive pathname deleter. Replacing the issued name with another ordinary directory redirects cleanup ownership. |
| Shared caller breadth | WorkspaceForkApplyService normal/failure cleanup and expired-fork enumeration call the same pathname-recursive deletion helper. Analyzer cleanup already has owned Windows/Unix namespace machinery to reuse or generalize. |
| Validation | Tests-first remediation authorized. No runtime proof or completion claim yet; production cleanup remains unchanged pending the parent's serialized causal run. |

## Context

- Exposed by the active operator-authorized restore initiative; remediate this complete shared cleanup mechanism before shipping.
- Keep this row open until verified and merged. Do not weaken directory identity checks or substitute a symlink-only fix.

2026-10-16 bounded parent runtime: corrected direct GUID-root stage disposal/failure five controls PASS; corrected approved-batch rebuild two controls PASS in a subsequent coherent run. Earlier disposal/failure root spellings selected nested content and are INVALID root-causal evidence; earlier guessed-coordinate rebuild premises are also superseded. Windows unlink-retry old regression reached denied unlink plus ordinary empty replacement, then incorrectly deleted replacement; Unix old retry and workspace-level sweep likewise deleted empty replacements. Fresh cleanup five controls PASS on Windows and Linux after immutable identity retention and checked held-child removal. Pure controls no longer initialize native sample services. Actual full SampleSolution fork cases timed out before replacement runner, so old-fork causal remains UNPROVEN and full-workflow timeout unresolved. Exact old fork service preimage is retained for guarded causal validation. Current one-project native cleanup fixture is source-only; no current-head aggregate, acceptance closure or merge claim.

2026-10-16 native fork causal now valid on exact old service preimage 84FC9FC6B6A74155615737E90725AC231815AA7E43433915179E486D345D1B1B with real one-project document-delta fixture: root replacement FALSE row FAILS sentinel preservation after actual restore runner, ancestor TRUE row PASSES (Windows rename-denial/normal cleanup); exactly one old failure, not two. Both fixed small-fixture rows passed the prior parent 25-case gate. Exact current service 5F60 preimage restored per fork-old-boundary-causal.receipt.json. Full SampleSolution timeout remains unresolved; bounded phase probe awaits parent execution. Row remains open, no full-aggregate or merge claim.

2026-10-16 cold review: PhysicalDirectoryScope.RemoveHeldChild124-131 releases checked handles before name unlink; a replacement in that gap can lose an unrelated empty directory. Existing retry tests require exact original-object ownership. Decision proposal ai_docs/plans/20261016-unix-cleanup-ownership-decision.md maps Windows handle-bound disposition and recommended POSIX final-name retention, all staging/recreation/sweeper/DTO consequences and alternatives; operator decision pending. Deterministic actual-filesystem dispose-triggered replacement fixture prepared in scratch only, uncompiled/unrun. No production fix or acceptance claimed; row remains OPEN.

2026-10-16 LIVE operator approved conservative POSIX final directory-name retention per ai_docs/plans/20261016-unix-cleanup-ownership-decision.md. Parent observed real dispose-triggered namespace replacement deletes unrelated empty replacement on Windows and exact-DLL Linux OLD controls (see parent runtime receipts; no green acceptance claim). Current shared core source installs required PhysicalDirectoryCleanupOutcome Removed/Retained, Windows live-object witness plus same-DELETE-handle disposition, no POSIX final directory unlink, and no unsafe Recreate. Stage/fork callers migrated to fresh coordinates and typed retained outcomes. Analyzer retirement/sweep propagation and cross-platform test migrations pending; joint initial compile not yet run. Row remains OPEN until full validation and merge.

Evidence precision correction: this agent received the parent Windows OLD failure at replacement-preservation after actual replacement. Linux exact-DLL execution was queued by parent, but its completed result was not delivered to this agent at this checkpoint; do not treat the preceding Linux wording as independently observed acceptance. Operator approval and source-only implementation status remain unchanged.

Parent subsequently delivered completed Linux OLD receipt: physical-final-unlink-old1-linux.trx, 1 valid failure in 51ms at replacement-preservation line45 after actual replacement. Exact copied product SHA4ac0205ae0a586232d42677b8d301648e947638429b0bafaf724b8870daac5a2 and tests SHAbcb00822fa76adc8c16ad2aac02be18516f7a5026ac85a726107d786fa2f5b94; SDK10.0.400 asserted by parent. This confirms cross-platform OLD defect; fixed-core runtime still pending.

2026-10-16 same final-delete mechanism: parent root20941 observed BOTH OLD file/link replacement controls fail after actual selected file/junction moved and ordinary empty directory replacement created (replacement missing at assertion line96). Windows DeleteFile had fallen through to name-based Directory.Delete. Current source removes that fallback: file/link and witnessed-directory cleanup reuse one DELETE-handle disposition ticket; current bound physical directory is refused in file/link mode before attributes or deletion. Required CancellationToken migrated to every scope DeleteFile caller/forwarder, including acquisition fixture. Source-only fix awaits fresh parent runtime; row OPEN.

2026-10-16 shared deletion-ticket metadata ownership: parent native probe verified FileDispositionInfoEx class21 flags0x11/0x13 on actual NTFS+ReFS, all4 preserve externally held readonly attributes and bytes while removing owned hardlink. Parent OLD managed hardlink test validFAIL at55: held external attrs ReadOnly|Archive became Archive; ordinary readonly control and fixed file/link/final removal controls passed (4PASS1FAIL,78ms). Current shared ticket uses only0x11 DELETE|IGNORE_READONLY, no extra POSIX flag; removes SetAttributes, legacy boolean disposition struct and WRITE_ATTRIBUTES access. No clearing fallback; current source awaits fresh managed validation. Row OPEN.

Parent verified bounded current cleanup gates: acquisition-hardlink-drain1 cleanup13 PASS (hardlink2, Windows atomic acquisition3, final3,retry5); exact copied DLL Linux physical-acquisition-fixed3 11 PASS/0skip/84ms, SDK10.0.400 and cmp/hash receipts. These focused results do not close full-gate/merge acceptance. Same owned-inode authority mechanism exposed in shared stage/fork writers: path Copy(overwrite)/WriteAllBytes can mutate a surviving external hardlink inode. New actual CopyPlan hardlink negative +ordinary overwrite positive +swapped-ancestor safety control tests saved, OLD runtime pending; shared held-root-relative read/write/delete/create API design in progress, source writers unchanged. Keep OPEN.

Owned-writer mechanism: parent verified Windows OLD3=2 PASS/1 hardlink overwrite FAIL (external byte101 became112 after reviewed target bytes succeeded); exact same DLL Linux SDK10.0.400 OLD3=2 PASS/1 identical FAIL, no skips. Shared issued-root writer/caller source now uses nofollow held parent traversal, exclusive fresh inodes, bounded copy streams, native Windows copy metadata/ADS plus live-witness same-object overlay publication, Unix exact fd timestamps/rwx, and terminal writer revocation; source only, focused runtime pending. Prior cleanup qualification remains Windows13 PASS and same-DLL Linux11 PASS; operator-approved POSIX final directory retention remains unchanged; row OPEN.

Focused writer/core qualification, parent-run this session: Windows writer-core-fixed1=21 PASS/1 Unix-only skip, 0.7964s; exact same DLL Linux SDK10.0.400 applicable18 PASS/0skip, 65ms, cmp/hash verified by parent. Covers external hardlink overwrite preservation, native Windows ADS/attrs/mtime and original overlay creation time, live-witness handoff plus bound no-replace rename, actual held-parent replacement actor, and writer revocation after disposal/failed cleanup with cleanup retry retained; Linux mode fidelity passes. Root independently matched Linux nanosecond timestamp/rwx/EPERM and macOS streamed-data+metadata7 source contracts to .NET v10 pal_io.c; independent cold review/full authoritative gates remain pending and row stays OPEN.
