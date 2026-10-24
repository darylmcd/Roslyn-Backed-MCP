# fork-evaluated-source-isolation — Keep evaluated fork paths out of source

**row:** `fork-evaluated-source-isolation` · **pri:** `Medium` · **size:** `L` · **deps:** `restore-required-project-inventory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:128`
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs:14`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:73`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:51`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:63`
- `tests/RoslynMcp.Tests/Workspace/ForkGraphPreparationTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`

## Acceptance

- [ ] Before restore/runner effects, evaluate the copied transitive restore inventory and reject source-resolving imports, file inputs and restore outputs, including non-compilation dependencies and required framework/global contexts.
- [ ] Before each known SDK load/build consumer's effect, check its actual native context and consumed paths. Cover package preprocessing (including non-copy FileWrites), assets-cache writes and local/reference copies; preserve native reference selection and valid external paths.
- [ ] Preserve conditional authored imports, first child metadata evaluation and cached continuation, retained-fork reload/reopen/test, and fork-of-retained-fork behavior. Keep source/fork configuration valid for its existing lifetime; preserve cancellation and originating time budgets.
- [ ] Use unchanged previews and real native effects. Preserve source bytes, Solution/version/snapshot/full preview authority; demonstrate unsafe old behavior and valid controls, including native RID/framework selection and Private/transitive copying controls.
- [ ] Retain the hidden .proj old-runner regression; obtain independent review, scoped and hosted checks, the full CrossProjectRefactoringIntegrationTests class with MSTest.Parallelize.Workers=1 and MSTest.Parallelize.Scope=ClassLevel, and the serialized just ci delivery gate (CI_POLICY.md) before closure.

## Scope

- Ship the complete source-isolation behavior; evaluator/task/import scaffolding alone is not completion.
- Reuse the restore evaluator and shared physical checks. Native task/import integration is required by the real public preprocessing effect and actual child-copy contexts.
- Keep restore scheduling distinct from build contexts. A build-only path is checked before its actual consuming effect; it does not require refusing an otherwise valid restore command.
- Preserve SDK task bodies and selection. No replacement BuildHost, second restore evaluator, physical-directory lease or native compiler certificate.

## Evidence

| Evidence | Result / limit |
|---|---|
| Original hidden-project regression | restore-fork-closure-corrected-red.log reached the old runner with a source output from a non-compilation .proj; absence from Roslyn was asserted. |
| Public preprocessing defect | preprocess-public-2.trx and the stronger design-time context matrix demonstrated real unchanged-preview keep/runTests=false source writes. preprocess-integration-1.log and preprocess-retained-1.log each passed4, including actual refusal, valid native outputs, reopen and later CLI protection with full source/preview authority checks. |
| Native consumer effects | native-build-product-real-red.log demonstrated a source DLL write despite successful native Build. Native output, RID/framework/local-copy, preprocessing membership and retained invocation matrices then passed; native-completion-cohort-2.log passed150 before subsequent corrections. |
| Final consumer corrections | native-final-consumer-red.log:15PASS6FAIL; native-final-consumer-green.log:21PASS0FAIL. Covers safe late mutation, marker current/prior flags, both manifest branches and missing-config public reopen. |
| Final bounded controls | native-finite-coverage.log:34PASS0FAIL, including28 actual native metadata/mode/casing/TargetPath/Link controls,3 import-install cancellation boundaries, actual native-command cancellation and owned-versus-inert retained-material discovery. |
| Lifetime and refusal | native-retained-malformed-cleanup-red.log:2PASS2FAIL; native-retained-cleanup-green.log:6PASS. native-loader-setup-red.log proved unstarted ownership leakage before correction. native-supported-stage-cancellation-1.log passed actual stage cancellation with exact owned BuildHost exit. Physical alias and cleanup faults are covered without cross-process ownership claims. |
| Independent implementation review | Fresh full cumulative review returned0BLOCKING after the bounded corrections; final formatting delta, exact committed-head scoped/hosted checks and full serialized delivery validation remain required. |
| Packaging | Final portable Windows/Linux10.0.402 and exact Linux10.0.400 CLI and actual BuildHost matrices passed6 each; current-native-final-windows.log, current-native-final-linux402-lf.log and current-native-final-linux400.log. Bounded activation/path checks do not replace the full gate. |
| Delivery state | Pre-commit checkpoint: PR1823 draft at published57d6a097; implementation reviewed but uncommitted. No row is closed; full CrossProject, exact-head scoped, hosted and delivery gates remain required. |

- Corrected old copy assertions distinguish restore inventory from actual build consumers; stronger native-effect controls preserve the original parent-destination and source/preview authority oracles.
- Native task/import wiring is forced by demonstrated actual load/build effects. No replacement BuildHost, compiler certificate, physical-directory lease or second restore evaluator is introduced.
- Graph fidelity remains owned by dependent restore-fork-reviewed-project-graph. Independent cross-process expiry coordination is a separate follow-up, not a restore prerequisite.
