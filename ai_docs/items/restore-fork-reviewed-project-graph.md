# restore-fork-reviewed-project-graph — Replay and restore the reviewed fork graph

**row:** `restore-fork-reviewed-project-graph` · **pri:** `Medium` · **size:** `L` · **deps:** `fork-evaluated-source-isolation,document-set-project-file-undo-capture`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:248` — copy/replay admission; strict version check324; raw restore770 (invocation796); document-only replay848.
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs:46` — native copied-graph evaluation and source isolation.
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:26` — shared native inventory; nullable-root planning134; invocation rendering256.
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:838` — existing shared source preview rebase implementation (apply boundary314).
- `src/RoslynMcp.Roslyn/Services/ProjectReferenceMutationRenderer.cs:14` — shared physical project mutation planning; policy decision71.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs:14` — existing landed fork preservation tests.

## Acceptance

- [ ] Capture every relevant source project's nullable package root before copying fork exclusions. Reuse the shared required restore plan; preserve platform identity, one target invocation for uniform roots, dependency-order invocations with --no-dependencies for mixed roots, and one continuing total timeout/cancellation budget.
- [ ] Rebase a representable ordinary reference-changing preview onto current source state; retain sibling edits and reference metadata. Prepare the shared physical mutation plan before creating/copying a fork; refuse concrete unrepresentable snapshots before copy or runner effects without blanket-refusing representable references.
- [ ] Replay that intended physical project/document plan, then verify copied/evaluated contexts, reference/item metadata and Compile inventory against the reviewed intended graph and source-isolation boundary before restore. Preserve encoding, rollback, source snapshot/bytes/version/token/Undo authority and later source redemption.
- [ ] Observe real public move, extract-interface and dependency-inversion producers fail on old behavior. Compare reviewed project-file effects to actual fork and source graphs; exercise uniform/mixed/missing roots, rebase, ownership, cancellation/deadline, reload, expiry, no-copy refusal and failure rollback. Preserve all landed apply/undo/rebase/diff cases.
- [ ] Complete fresh independent plan/implementation reviews, the complete serialized producer and hosted Windows/Linux/SDK-floor qualification. Partial or earlier scoped green receipts never close this row.

## Scope

- One complete intended-snapshot projection contract: source capture, preparation, fork replay and fidelity verification ship together when independently correct.
- Re-derive and decompose this L remainder into independently correct dependency-chained implementation slices before writing code. Preserve every acceptance obligation during decomposition; five current production anchors and the preserved unmerged helper describe the investigation boundary, not an approved six-file patch.
- File counts guide reviewability; the obsolete four-file cap does not justify a partial repair.
- Reuse existing rebase, renderer and native evaluator seams. No copied service implementation, new graph walker, owned BuildHost, compiler certificate or directory lease machinery is authorized.
- Landed source-isolation and document-encoding protection remains authoritative. Recheck all future dependencies against tests; parent closure is not evidence this row or its future prerequisites are satisfied.
- Retain the existing source-isolation and physical-undo dependency IDs as delivered history; both implementations are merged. No dependency on the parent restore row is added.

## Evidence

| Evidence | Meaning / limit |
|---|---|
| [Unmerged graph helper](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/0062d60ca47a0b08d56d1c91feac333b67de876a/src/RoslynMcp.Roslyn/Services/RestoreProjectGraph.cs) | Preserved branch proposal only; absent from main. |
| [Unmerged graph regression](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/0062d60ca47a0b08d56d1c91feac333b67de876a/tests/RoslynMcp.Tests/Workspace/WorkspaceForkReviewedGraphTests.cs) | Preserved branch tests only; absent from main. |
| restore-fork-red.log; e2-causal-initial-red.log | Public producer loses reviewed reference/root behavior; strict token guard is a separate observed refusal. |
| Published PR1826 at0062d60c | Preserved proposed graph work only. Hosted failures and incomplete native Compile/import/authority qualification prohibit landing this bundle. |
| e2-native-default-policy-old-causal.log/current-causal.log | 24 cases:18 failed/6 passed; distinguish15 policy failures from3 undo failures. No post1828 qualification claim. |
| Native-policy/encoding/reload rows | document-set-native-default-compile-policy; new-file-apply-writes-bom-ignoring-editorconfig; workspace-explicit-reload-double-transition retain different mechanisms. They do not block the parent restore closeout. Future graph feasibility must be re-derived. |
| Preserved WIP | Existing PR1826 branch/dirty worktree and15-file frozen manifest under `<user>/.codex/scratch/roslyn-remediate-20261023-split/`; b88d8c/PR1809, origin/codex/restore-reviewed-graph/PR1806, codex/restore-callers-complete450c75dd and stash codex-remediate-20261022-preserve-restore remain references. No code copied across. |

## Parent scope transfer

| Original restore-callers-missing-packages-path clause | Open owner here |
|---|---|
| Bullet1 fork use of shared nullable-root plan | First acceptance bullet. Source workspace/test-runner caller fixes are already delivered. |
| Bullet2 source assets before exclusions and intended copied graph fidelity | First and third acceptance bullets. Test-runner restore-before-authorization is delivered. |
| Bullet3 fork rebase/shared renderer/pre-copy representability/source authority | Second and third acceptance bullets. Shared renderer/reference apply-rebase, physical diffs and write-path visibility are landed; separate native-policy and source-encoding rows remain open. |
| Bullet5 real fork/source producer equivalence | Fourth acceptance bullet. Landed caller/kind/layout/root RED receipts remain preserved separately. |
| Bullet6 qualification of the unmerged fork contract | Fifth acceptance bullet. Parent delivered-scope qualification cannot prove this contract. |

This is an independent open remainder under the operator's finite closeout directive. Closing the parent records the delivered source restore fixes and an explicit scope transfer; it does not mark the original fork clauses met.
