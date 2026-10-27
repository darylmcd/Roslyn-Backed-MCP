# restore-fork-reviewed-project-graph — Replay and restore the reviewed fork graph

**row:** `restore-fork-reviewed-project-graph` · **pri:** `Medium` · **size:** `L` · **deps:** `fork-evaluated-source-isolation`

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
- [ ] Each test scenario in Evidence is red on main, or recorded as a passing control, before implementation.
- [ ] ADR plus CHANGELOG migration note for fork rebasing stale previews instead of refusing them, and for refusing previews that carry project XML as a document.

## Scope

- One complete intended-snapshot projection contract: source capture, preparation, fork replay and fidelity verification ship together when independently correct.
- Re-derive and decompose this L remainder into independently correct dependency-chained implementation slices before writing code. Preserve every acceptance obligation during decomposition; five current production anchors and the preserved unmerged helper describe the investigation boundary, not an approved six-file patch.
- File counts guide reviewability; the obsolete four-file cap does not justify a partial repair.
- Reuse existing rebase, renderer and native evaluator seams. No copied service implementation, new graph walker, owned BuildHost, compiler certificate or directory lease machinery is authorized.
- Landed source-isolation and document-encoding protection remains authoritative. Recheck all future dependencies against tests; parent closure is not evidence this row or its future prerequisites are satisfied.
- Delivered prerequisites: `fork-evaluated-source-isolation` and `document-set-project-file-undo-capture`; both implementations are merged. No dependency on the parent restore row is added.

## Evidence

| Evidence | Meaning / limit |
|---|---|
| [Unmerged graph helper](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/0062d60ca47a0b08d56d1c91feac333b67de876a/src/RoslynMcp.Roslyn/Services/RestoreProjectGraph.cs) | Preserved branch proposal only; absent from main. |
| [Unmerged graph regression](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/0062d60ca47a0b08d56d1c91feac333b67de876a/tests/RoslynMcp.Tests/Workspace/WorkspaceForkReviewedGraphTests.cs) | Preserved branch tests only; absent from main. |
| restore-fork-red.log; e2-causal-initial-red.log | Public producer loses reviewed reference/root behavior; strict token guard is a separate observed refusal. |
| Published PR1826 at0062d60c | Preserved proposed graph work only. Hosted failures and incomplete native Compile/import/authority qualification prohibit landing this bundle. |
| e2-native-default-policy-old-causal.log/current-causal.log | 24 cases:18 failed/6 passed; distinguish15 policy failures from3 undo failures. No post1828 qualification claim. |
| Native-policy/encoding/reload rows | document-set-native-default-compile-policy; new-file-apply-writes-bom-ignoring-editorconfig; workspace-explicit-reload-double-transition retain different mechanisms. They do not block the parent restore closeout. Future graph feasibility must be re-derived. |
| Preserved WIP | Design and scenarios inlined 2026-10-27 (see Evidence); archive branches, worktrees and stash deleted. PR heads remain at GitHub `refs/pull/1806/head` and `refs/pull/1826/head`. |
- 2026-10-27: the preserved PR1826 test edits are inlined in this row; their archive branch was deleted (PR1826 head stays at GitHub `refs/pull/1826/head`).
- 2026-10-27, design inlined from closed PR1826 (head `0062d60c`, GitHub `refs/pull/1826/head`); re-derive, do not port:
  - (1) Under the source writer, before creating the pool, refuse a snapshot that adds/removes a project; changes project identity (path, name, assembly, output, namespace, compilation/parse options, metadata or analyzer references); changes any additional or analyzer-config document; renames a changed document or changes its kind, folders or path; or has any path outside the source root.
  - (2) Bind each changed/removed document and each added/removed reference target to exactly one current project or document, by id then by path with equal name/assembly/output/parse options; refuse missing or ambiguous bindings.
  - (3) Rebase onto current state with the shared `RefactoringService` rebase: project XML from `ProjectReferenceMutationRenderer.PlanAsync`; document bytes = preamble + text encoded with `SourceFileEncoding.FromSourceText`; deletions null. Refuse when linked or multi-target contexts need contradictory bytes or membership, or a removed path keeps a surviving owner.
  - (4) Before copying, evaluate the intended graph through an MSBuild `EvaluationContext` over an in-memory overlay filesystem. For each inner-build context: added paths must be evaluated `Compile` items, removed paths must not. Refuse reference globals containing a source-root path (`SolutionDir`/`SolutionPath` exempt; `CurrentSolutionConfigurationContents` parsed and its `AbsolutePath` values mapped). Capture each node (full path + sorted globals) with edges, `ProjectReference` and `Compile` items and evaluated metadata; plan restore invocations from this graph.
  - (5) After copy and replay, re-evaluate the fork, map paths back, and require identical nodes, edges, reference items and `Compile` items; a mismatch names its category and refuses before any runner call.
  - (6) Relocate the restore target and `-p:` values (including solution-configuration paths) from source to fork; never relocate `--packages`; one budget covers preparation plus restore; rebase replaces the stale-version refusal.
- Test scenarios (Source+Shared `.slnx` fixture restored with explicit `--packages`): public move Source→Shared with/without a sibling format apply (fork `Source.csproj` references Shared, `Scalar.cs` only in Shared, project bytes = renderer output + owned admission Import, sibling bytes survive, source snapshot/preview/undo/bytes unchanged, later apply equals fork, undo restores); recorded roots (no fork `project.assets.json` at first runner call; uniform roots = 1 call with `--packages` = source root and no `--no-dependencies`; mixed = 2 calls with own roots and `--no-dependencies`); refusals with 0 runner calls, no pool and preview retained for: `ProjectReference` conditioned on `MSBuildProjectDirectory`, an additional-document snapshot, an added document excluded by `DefaultItemExcludes` or `Compile Remove` (single and multi-target), a linked document with contradictory text/encoding/removal, a missing binding, an expired `RequestDeadline`, a removed document still included by an imported literal `.targets` item; linked owners that agree (UTF-16 bytes, both owners load); success controls (default, `EnableDefaultCompileItems=false`, unrelated/inactive `Compile Update`, matching `Link`, `%(Filename)`-conditioned metadata, nested directory); same-path remove+add as UTF-16; hidden closure (missing assets × multi-target: conditional `ProjectReference` `Exists('Nested/Marker.cs')` with `AdditionalProperties` `Flavor=Blue;ExternalInput`, restore Hidden then Source with `--no-dependencies`, Hidden gets `-p:Flavor`/`-p:ExternalInput` and its own recorded root, never `-p:TargetFramework`); caller cancellation between per-project restores launches no further restore.
- Hosted run 40152154908 at `0062d60c` (Windows 1/4 and both Linux legs red): `ForkNativeAdmissionTests.RetainedFork_ProjectFilePreviewReplay_StripsOnlyPreviousOwnedImport` stored the `.csproj` itself as an added document and hit the new Compile-membership refusal (implication: project XML is never replayed as a document); `WorkspaceForkGraphReplayTests.UnchangedPreview_OriginalExternalProject_RefusesBeforeRunner` changed its message to "outside its copied source root"; hidden closure on Linux only: 16 × CS0246 `System` in `Hidden/obj/Debug/net8.0`, cause unresolved (see `workspace-native-reference-global-context-mismatch`).

## Parent scope transfer

| Original restore-callers-missing-packages-path clause | Open owner here |
|---|---|
| Bullet1 fork use of shared nullable-root plan | First acceptance bullet. Source workspace/test-runner caller fixes are already delivered. |
| Bullet2 source assets before exclusions and intended copied graph fidelity | First and third acceptance bullets. Test-runner restore-before-authorization is delivered. |
| Bullet3 fork rebase/shared renderer/pre-copy representability/source authority | Second and third acceptance bullets. Shared renderer/reference apply-rebase, physical diffs and write-path visibility are landed; separate native-policy and source-encoding rows remain open. |
| Bullet5 real fork/source producer equivalence | Fourth acceptance bullet. Landed caller/kind/layout/root RED receipts remain preserved separately. |
| Bullet6 qualification of the unmerged fork contract | Fifth acceptance bullet. Parent delivered-scope qualification cannot prove this contract. |

This is an independent open remainder under the operator's finite closeout directive. Closing the parent records the delivered source restore fixes and an explicit scope transfer; it does not mark the original fork clauses met.
