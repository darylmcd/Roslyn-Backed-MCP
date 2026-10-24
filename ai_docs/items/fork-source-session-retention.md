# fork-source-session-retention — Preserve the source session throughout fork apply

**row:** `fork-source-session-retention` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:302` — LRU selection and removal at469/522; missing-root retirement553; disposal1108.
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:117` — source capture ends137 before restore141 and LRU load143.
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:26` — actual interface manager registration and fork-service binding157.
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceSessionTests.cs` — real rename preview, native restore and source-authority preservation at capacities1/2.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceCapLruEvictionTests.cs`
- `tests/RoslynMcp.Tests/Workspace/MissingWorkspaceRootRetirementTests.cs`

## Acceptance

- [ ] Retain the exact source session from before source-context capture until every fork outcome and cleanup completes; preserve its Solution, version, bytes and full preview authority absent authorized concurrent mutation.
- [ ] At capacity1, refuse the fork without evicting its source; with an eligible idle workspace, evict that workspace instead. Atomically recheck late retention at removal and continue to later eligible candidates.
- [ ] Refuse explicit close before side effects while retained; postpone missing-root retirement until release. Release retention on cancellation, refusal, validation and cleanup failure.
- [ ] Coordinate shutdown and in-flight removals: cancel outside lifecycle synchronization, asynchronously drain owned work before resource teardown, attempt cleanup exactly once and propagate failures. Remove the touched synchronous retirement join; synchronous disposal must safely defer pending teardown without unobserved failure.
- [ ] Bind retention to the actual resolved manager, including Host interface overrides; preserve existing source-write concurrency during native restore, validation and tests.
- [ ] Observe causal regressions fail on unchanged main, pass the full affected lifecycle/fork suite, obtain independent review and green hosted Windows/Linux/SDK-floor checks. Close only after verified merge and the serialized delivery gate.

## Evidence

- Base `dad299cfbd986486309bbf1ab76a8918a7f3dcd6`: two real public fork tests FAIL, zero PASS/SKIP; both fork creations succeed but evict their source and invalidate its complete real rename PreviewEntry.
- Capacity2 retains the other idle workspace instead of the source; manager logs name the source LRU victim.
- Raw receipts: `<user>/.codex/scratch/roslyn-remediate-20261023-split/source-session-main-oldred-final.log` and `source-session-main-oldred-final/source-session-main-oldred-final.trx`.
- Checkpoint: `<user>/.codex/scratch/roslyn-remediate-20261023-split/source-session-main-oldred-checkpoint.md`.
- Cold-reviewed plan: Section A of `<user>/.codex/scratch/roslyn-remediate-20261023-split/plan-source-session-and-storage-order.md`; reader found zero blocking plan defects. Implementation remains unverified.

## Context

- Independent main-branch lifecycle defect; required before the PR1823 source/storage lock correction can preserve source authority correctly.
- Scope: manager lifecycle, fork callback and binding only. No physical-storage, native-authority or graph-replay code copied from PR1823/1806/1809.
- Keep the existing source-write concurrency regression unchanged; do not hold the source writer across restore, validation or tests.

## Composition amendment

- Give the fork service ownership of cancellation and draining for its active applies before provider disposal reaches earlier dependencies.
- Never stop or dispose an externally supplied manager.
- Prove active Host shutdown, external-manager reuse and reentrant disposal.
- Migrate actual resolving providers to async disposal; give this piece its own decision and release note.
- Cold design review: zero blocking concerns. Implementation remains unverified.
