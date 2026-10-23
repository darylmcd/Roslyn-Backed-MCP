# Prerequisite session handoff - 2026-10-16

| Workflow status | Current direction |
|---|---|
| Historical engine commands below are superseded | Use the installed `~/.claude/prompts/remediate.md`, `~/.claude/HELPERS.md` and repository `CI_POLICY.md`; machine validation uses `just machine-validate` or the shared validation-gate wrapper. |
| Evidence | Preserve historical results; do not invoke the retired executor/state commands as current instructions. |

Expand %USERPROFILE% to the Windows user profile and /home/<user> to the WSL user home.

## Operator scope and stop condition

- Current session stopped at the operator's checkpoint request. No implementation may resume in this session.
- New session: finish RESTORE, then EXTRACTION; verify and ship each through green local/hosted gates and merge.
- Finish with clean current main and no open PRs, then STOP. Do not advance the 15-item batch.
- Preserve the existing 15-item branch and worker worktrees. Prior batch work predates this stop instruction; it is not a completed deliverable.
- Do not use the backlog-remediate skill. Global canonical helpers remain allowed.
- Delegate source work if useful. Serialize machine-global builds/tests/native probes; parent alone ran them in this session.
- Re-derive inherited claims. Do not weaken tests, metadata/path equality, current grants, or required gates.

## Preserved source state

| Work | Path / branch / HEAD | State |
|---|---|---|
| Primary | D:/Roslyn-Backed-MCP; main; 000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7 | Clean at checkpoint; fetch origin --prune succeeded; recheck before resume |
| Restore | D:/Roslyn-Backed-MCP/.worktrees/restore-callers-missing-packages-path; codex/restore-reviewed-graph; e5edef1272822a4b1edca417d42dbaa67cf5c59b | Large intentional UNCOMMITTED WIP, including untracked production/test/docs files; latest source compiles but tests RED |
| Extraction | D:/Roslyn-Backed-MCP/.worktrees/extract-type-preserve-untouched-trivia; codex/extraction-semantic-preservation; 9e378940e701efd32f2ebcccdc00cf13761cbeab | Preserved WIP; compiled production adapter not integrated |
| Paused 15 | codex/backlog-correctness-15; 3e9b280054083cc077d6291cea921975753c2643 | Do not advance; retain associated cohesion/store/workspace worker worktrees |

No new commit, push, PR, or merge was performed in this continuation. Open PR listing returned [] at checkpoint. All subagents finished; final focused test process exited. No full gate is running.

## Exact latest red result - start here

| Evidence | Result |
|---|---|
| %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/content-replay-first1/content-replay-first.trx | 15 total: 13 PASS, 2 FAIL, 2.8609 minutes |
| Same parent directory plus .log | Complete build/test output |
| PhysicalGraphContentReplayTests.PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(true) | Fails line70: actual package import must remain external. Native stage creation and reference mapping passed before this assertion. false/external-cache row passed. |
| ForkInvocationManagerAuthorityTests.ActualTestProcess_UsesOriginalRootAndCacheGrantsThroughValidation | Fails after 2m50 with PhysicalGraphMismatchException, assembly/analyzer references differ, PhysicalGraphStage.cs:353. Real test-process success is NOT proved. |

The other 13 pass cases cover InstalledPackageAuthority tests, physical cleanup3, the fresh stage native Exists/glob control, and the external-cache replay row. Do not retry this unchanged red tree.

## Confirmed replay root cause and incomplete fix

- SDK default None items include a package cache nested under the project. Native ResolveAssemblyReference checks CandidateAssemblyFiles before HintPath.
- Exact guarded native probe: %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/mstest-native-candidates-69341a9a960b4ec2b699f068c466fbf5.
- Actual source RAR candidates: 793 string entries, 20 known MSTest DLL variants; staged candidates: 18 string entries, zero known variants.
- Both actual RAR HintPath lists select net9 variants; actual Csc source selects net462 while stage selects net9.
- Both diagnostic source files were restored byte-for-byte. No diagnostic code remains installed.
- Installed A slice adds required PhysicalGraphLayout.ContentReplayFiles, copies exact witnessed overlapping native item roles, preserves independent immutable provenance and current-grant requirements, skips XML rendering of package files copied only as content. Missing item identities remain frozen.
- InstalledPackageAuthority now retains immutable exact membership and RequiresCurrentGrant per issued proof. It caches no permission decision or file freshness.
- KNOWN DEFECT: MapInputPath's replay membership override also rewrites absolute generated NuGet Import operands into the private content copy. This violates the external-import requirement.
- Simply restoring immutable early-return also breaks the source-to-private selected-reference comparison. Do not replace one failure with the other.

### Next design is a proposal, NOT installed or approved as correct

- Separate definition/import coordinate mapping from native relative-content/reference coordinates.
- Consider two deterministic coordinate functions for exact graph comparison, normalizing both sides to original coordinates.
- Preserve mandatory private placement of mutable inputs. Do not blindly reverse-normalize an accidentally external mutable input and accept it.
- Bind any immutable original/private equivalence to exact source and private bytes, manifest/package proof, and current grants; no either-path allowlist or weakened comparator.
- Add real native explicit absolute-item-only/mixed-role control. Preserve the current failing import assertion.
- Re-derive all comparer callers, compilation options, document paths, generated config, reference metadata, aliases and interop before choosing the complete API change.

Detailed call inventory/proposal: %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/mstest-content-replay-next-session.md. Its old running-test/lifecycle notes are superseded by THIS report. A-file hashes: mstest-content-replay-checkpoint.json in the same scratch directory. No partial dual-mapper migration was saved; source remains compilable.

## Verified prerequisite fixes to preserve

| Mechanism | Current-session evidence |
|---|---|
| Fresh owned directory acquisition; identity-bound deletion; readonly hardlink preservation; writer lifetime revocation; bound stage/fork writes | writer-core-fixed1/writer-core-fixed.trx: Windows21 PASS, Unix-only1 skipped; exact same DLL Linux applicable18 PASS/0skip, physical-writer-core-fixed1-linux.trx under /home/<user>/.codex/scratch/roslyn-correctness15 |
| File-copy/overlay fidelity | Windows source copy and overlay preserve ADS, basic attributes, timestamps/creation time without changing external hardlink bytes; Linux executable mode passed |
| Windows file ownership publication | Atomic fresh file pin; native copy; live same-object witness bridges pin release to DELETE-handle rebind; validate both identities before old-entry effects; relative no-replace rename |
| Manager registration/load/close/shutdown/host drain/retirement | lifecycle-drain-fixed2/lifecycle-drain-fixed.trx: all15 PASS, 2.4609 minutes; includes secondary drain failures retained on original primary with null reporter |
| Documentation preflight | restore-docs-owned-writer1.log: just verify-docs PASS (32 pins, notices, AI docs,38 shipped Markdown) |

All relative Windows evidence above is under %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15. These are focused results, NOT full-gate approval. Latest replay source changed after the writer floor; rerun appropriate integrated checks.

Canonical physical-graph-cleanup-path-authority row remains OPEN. A separate pre-existing workspace-retirement-dispose-reentrancy row is OPEN Medium; static risk only, no runtime/fix claim. Track replay regression precisely in the matching native coordinate/output row; do not silently omit it or close acceptance on current red results.

## Operator decisions already approved - do not ask again

| Decision | Contract |
|---|---|
| Supersede unfinished holds | Complete restore and extraction before new backlog work |
| Compiled base constructors | Implement compiled-code support |
| Extraction proof boundary | Conservative refusal before effects when semantic preservation cannot be established; some safe extractions may be rejected |
| POSIX cleanup | Reclaim owned contents, retain content-free directory skeletons, report retention, use fresh staging coordinates; Windows uses verified handle-bound deletion |

See existing decision documents/ADRs0028 and0029 in the worktrees. Native semantic degraded loading without compiler/image/physical authority is permitted under the recorded contract; do not impose blanket semantic-load bans.

## Extraction after restore lands

- Read %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/compiled-adapter-current-restore-reconciliation-20261016.md.
- Drafts: compiled-adapter-source-hooks.draft.patch; OwnedCompiledClosure.draft.cs; CompiledAnalysisSymbols.OwnedDraft.cs; TypeExtractionService.CompiledBinders.draft.cs.
- Current restore API matches ReadAdmittedImagesAsync(workspaceId, ProjectId, exact PE reference, ct), returning AdmittedModuleImage(FileName, ModuleName, MVID, immutable bytes). Do not assume a public hash property.
- Preserve eager owned image/module closure and manifest file hashes, aliases/interop, live source CompilationReferences, original declaration/callback mapping. Never expand the original lazy metadata factory or substitute runtime implementations outside admission.
- Reference-assembly stub bodies already refuse interpretation in CompiledExecutionModule.ReadBody. Existing BodyAdmission tests cover methods and constructors; only justified System.Object ctor intrinsic bypasses ordinary body analysis.
- Rebase after restore merge, integrate production adapter, run real TypeExtractionService safe stateful external-base, unsafe callback, and source-project/no-PE controls. Prior107 component passes are NOT adapter end-to-end proof.

## Full validation and shipping

- Finish focused/cold review and changed-file format check. Do not modify managed versions or publish NuGet as an unrequested release.
- Use ship skill with Codex overrides, exact staged-only pre/post guards, immutable verified commit push, explicit PR head/base, and attach created PR.
- Local and hosted gates can overlap AFTER guarded commit/push/PR. Mark local validation pending in PR body. Freeze HEAD/index/source while local producer runs; committing during it invalidates receipt.
- Required restore producer: node %USERPROFILE%/.claude/scripts/bsweep-state.mjs executor-full-gate --plan D:/Roslyn-Backed-MCP/.worktrees/restore-callers-missing-packages-path/ai_docs/plans/20261007T211247Z_backlog-remediate --id restore-callers-missing-packages-path --repo-root D:/Roslyn-Backed-MCP --worktree D:/Roslyn-Backed-MCP/.worktrees/restore-callers-missing-packages-path --log-dir <fresh absolute scratch directory>.
- Capture literal UTC epoch-ms before background launch. Poll executor-full-gate-status with same plan/id/repo-root and --launched-after <literal epoch-ms>. Running exit3 means keep waiting, not relaunch. Require passing receipt tree equals verified commit tree.
- Full unsharded aggregate is required; do not hand-slice the gate. Require all exact-head hosted validate/SDK-floor/CodeQL checks green before ship --land and canonical cleanup.
- Canonical state/backlog writers only. Never hand-edit audited state. Historical plan is in the RESTORE WORKTREE, not the stale primary copy.

## Checkpoint receipts

Detailed chronological evidence: %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/root-checkpoint-20261016-exact-encoding-fork-package.md.
Whole-worktree file hashes/status receipt: %USERPROFILE%/.codex/scratch/roslyn-20261015-correctness15/prerequisite-checkpoint-final.json.
This report is a documentary checkpoint. WIP is preserved in place; it is NOT a validated checkpoint commit or merge.
