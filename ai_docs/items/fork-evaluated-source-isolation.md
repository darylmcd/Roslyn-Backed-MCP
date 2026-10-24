# fork-evaluated-source-isolation — Keep evaluated fork paths out of source

**row:** `fork-evaluated-source-isolation` · **pri:** `Medium` · **size:** `L` · **deps:** `restore-required-project-inventory,fork-source-session-retention`

## Anchors

- `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`
- `src/RoslynMcp.Roslyn/Helpers/WorkspaceDiagnosticSeverityClassifier.cs`
- `src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`
- `src/RoslynMcp.Roslyn/Services/AdmitForkBuildPaths.cs`
- `src/RoslynMcp.Roslyn/Services/DiscoverForkTargetDependencies.cs`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/ForkAdmission.targets`
- `src/RoslynMcp.Roslyn/Services/ForkAdmissionMaterial.cs`
- `src/RoslynMcp.Roslyn/Services/ForkAdmissionTask.cs`
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/ForkPathAdmission.cs`
- `src/RoslynMcp.Roslyn/Services/ForkStorageDirectoryName.cs`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/InstallForkProjectImports.cs`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`
- `tests/RoslynMcp.Tests/Helpers/ForkStorageFixtureBoundary.cs`
- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj`
- `tests/RoslynMcp.Tests/ServiceCollectionExtensionsTests.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`
- `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkGraphPreparationTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkRetainedSourceDeletionTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkStorageBoundaryTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkStorageLockOrderTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkDocumentEncodingTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceSessionTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceForkGitIsolationTests.cs`

- `tests/RoslynMcp.Tests/WorkspaceForkApplyCancellationTests.cs`
- `tests/RoslynMcp.Tests/HostShutdownLifecycleTests.cs`

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
| Native effects | Public preprocessing and actual Build old-code regressions demonstrated source writes with unchanged real previews. Native RID/framework/reference-copy, FileWrites, conditional imports, retained invocation and valid external-path controls then passed. Raw receipts are retained in the PR handoff. |
| Prior exact published head | de61d096: 598PASS0FAIL0SKIP across25 classes, including full CrossProject19 and construction ratchet88, no cross-class overlap. Hosted Windows1 subsequently failed actual nested retained-fork CWD error267; three Windows, both Linux and SDK-floor400 passed. No unchanged retry. |
| Source lifetime dependency | PR1824 squash14fbdc025d5b767e568b9598936b6bb783add555 merged with independent review and all hosted Windows/Linux/SDK-floor checks green. Integrated locally by merge2faa75fb. Source retention and async disposal are inherited from main. |
| Actual reload lock order | Separate actual Host Reload old-code run:2FAIL. Physical source ownership before source writer and across fork load caused both rendezvous failures. New pool-only outer ownership and short physical source ownership inside writer allow reload to complete. |
| Stale source preview | First corrected cohort9PASS1FAIL: pre-copy reload completed but old-version preview was accepted. Compare actual manager version inside the source writer through existing PreviewTokenStaleException; final both reload phases pass. |
| Canonical pool copy | Pool equal to source old code failed PathTooLongException; descendant case aborted testhost in recursive copy. Dynamic physical exclusions prevent destination recursion while preserving authored imports/content and verified prior fork material. Aborted run is not passing evidence. |
| Default SDK source graph | Stronger ordinary default Compile fixture:2FAIL on old generated names. Original build passed before fork, then failed CS0101/CS0579 after generated children entered original SDK globs. Hidden reserved generated names preserve default SDK inputs. |
| Original preview watcher | Real Host graph, real rename and three native fork generations:2FAIL with bare-dot names because original source became stale. Shared narrow generated-name parser now excludes only reserved generated directories; authored hidden/lookalike events remain watched. |
| Linked cleanup | Logger-qualified old code:1FAIL5PASS. Recursive deletion threw UnauthorizedAccessException and changed external ReadOnly attributes. Shared iterative no-follow deletion now preserves external bytes/attributes and collects the owned directory. |
| Final implementer qualification | final-owned.log/TRX:129PASS0FAIL2existingLinux-onlySKIP131total5m04s. Includes actual canonical source/descendant pools, original native build before/after every generation, unchanged full source preview/Solution/version/snapshot/bytes and genuine same-token original apply/undo. Compile8/8zero. One subsequent XML-summary placement edit only. |
| Retained-source deletion | Fresh full review found cleanup deleting after failed Close and expiry deleting a retained source queued on pool. Actual unchanged-product races4FAIL0PASS27s; corrected principal4PASS40s, broader seven-class261PASS0FAIL0SKIP9m11s. Same-manager atomic deletion/source admission preserves nested owners and actual final-manager overrides without additional physical ownership machinery. |
| Repeated canonical source identity | Actual directory retarget old-guard regression failed at second registration. Preserve the original physical path before count mutation; real writer rendezvous proves first Apply refuses before copy, with no destinations and preserved authority. Setup-only alias and incorrect-rendezvous receipts are not causal product failures. |
| Final correction qualification | Writer-qualified final focused gate121PASS0FAIL0SKIP2m22s; compile8/8zero; authoritative fragment check120current1changed passed. Earlier120PASS1FAIL receipts retain the synchronous-DI fixture and alias checkpoint failures; both were corrected without product shims or weakened assertions. |
| Independent implementation review | Full cumulative review plus fresh correction review:0remainingBLOCKING. Final46authorfile hashes independently matched. These reviews and bounded passes do not replace exact-head affected or hosted gates. |
| Expanded qualification | Root clean d860bdce forty-class run779PASS2FAIL3existingLinux-onlySKIP784total17m07s; all40classes and zero overlap. Full CrossProject19 and ratchet88 passed. Two stale reflection-argument failures were retained as RED evidence, not retried unchanged. |
| Indirect helper caller correction | Sole reflective cancellation caller now supplies the required explicit destination exclusion, preserving every assertion. Independent review0BLOCKING; four-class66PASS0FAIL0SKIP includes both original failures and native source-inside/equal controls. |
| Hosted retirement ordering | Hosted Windows3 also exposed the new deletion test assuming the session survives actual watcher retirement. Hold the real writer to qualify exact missing-file and missing-root refusal, then release it and await actual WorkspaceClosed before exact missing-workspace/preview-invalidation assertions. No product changes, exception union or watcher suppression. Focused23PASS0FAIL0SKIP and independent review0BLOCKING. |
| Delivery state | Exact21-path WIP26157fc5 and reviewed production d860bdce remain published in PR1823. Clean32de7e8a serialized forty-class suite:781PASS0FAIL3existingLinux-onlySKIP784total18m09s, all40classes and zero overlap. All four Windows shards, Linux2, SDK-floor400 and CodeQL passed. Linux1 exposed the independent cancellation-fixture rendezvous below; its minimal correction is reviewed for the next exact head. No row closure or full serialized delivery-gate claim. |

- Real native load/build effects force task/import integration; no replacement BuildHost, native compiler certificate, physical-directory lease or second restore evaluator.
- Graph fidelity remains the dependent restore-fork-reviewed-project-graph slice. Independent cross-process expiry coordination and provider/root-retirement concerns are follow-up rows without restore dependencies.
- The root session owns the full serialized producer and just ci before closure, plus the final clean merged-main gate after trailing documentation.

| New correction evidence | Result / limit |
|---|---|
| Hosted legacy fixture correction | Windows1 positive two-level legacy roots exceeded native launchable cwd before public Apply. Same hosted-length90 TEMP reproduced2FAIL on unchanged45ff; remove only the diagnostic prefix for those two full-GUID positive roots, preserving topology and all default/long-negative controls. Two-case2PASS then full173PASS0FAIL0SKIP8m30s under the same long TEMP; valid-long-root, invalid generated cwd and258/259 controls passed. |
| Hosted startup fixture correction | Actual hosting10.0.13 can signal ApplicationStarted before BackgroundService worker admission. Controlled exact-base source probe showed Started false, cooperative IHostedService fixture true with the sole worker held; this does not establish historical hosted pool starvation. Actual both shutdown paths2PASS0FAIL0SKIP1s. StopEntered and pending-run assertions strengthen drain ordering, all five-second bounds remain, finally drains/disposes after assertion failures. |
| Bounded correction review | Independent reader:0BLOCKING for both test-only corrections, raw OLD/GREEN evidence and post-gate frozen hashes matched. Roslyn compile zero errors; changed formatter38C# files zero new or baseline findings. Production unchanged since d860bdce. New committed-head affected and hosted checks remain required before merge. |

| Cancellation fixture correction | Result / limit |
|---|---|
| Causal old behavior | Both actual32de fixture paths with queued continuations held nested cancellation callbacks and blocked operation drain until the original two-minute deadline. Releasing the callback let operation, stop and provider disposal finish; this proves the fixture cycle under that scheduling condition, not the historical hosted scheduler cause. |
| Minimal correction | Register a locally owned hold on the actual initiating lifetime CTS before linked registrations; retain asynchronous runner release, every original assertion/deadline and release-before-await cleanup. Actual operation.IsCanceled prevents confusing an expired test wait with operation completion. Remove the obsolete unregister switch. |
| Independent qualification | Exact14insert11delete test-only delta; fullLifecycle14PASS0FAIL0SKIP, semantic compilation zero errors/warnings, formatter38C#zero new/baseline findings. Fresh actual-diff review0BLOCKING; fixture/watcher/gate cleanup blocks match32de exactly. New committed-head affected and hosted gates remain mandatory. |
| Independent cleanup split | Preserve the experimental cleanup draft and receipts; none of that rewrite or its three added tests remains in this PR. Pre-existing cleanup mechanisms are filed as fork-source-fixture-cleanup-failure-propagation, then fork-source-fixture-initialization-cleanup-ownership; test-workspace-unwatch-barrier-timeout-assertion also depends on the first row. These rows are in a separate docs-only branch and are not restore prerequisites. |

