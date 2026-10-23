# native-test-cleanup-primary-preservation — native-test-cleanup-primary-preservation

**row:** `native-test-cleanup-primary-preservation` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/AdmittedModuleImageTests.cs`
- `tests/RoslynMcp.Tests/NativeConditionProtocolTests.cs`
- `tests/RoslynMcp.Tests/PhysicalSourceTransitionTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerAssemblyBindingTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerInputCatalogTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerDirectoryRetentionTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowSnapshotLifetimeTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowInputAuthorityTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowSweepAuthorityTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentCurrentAuthorityTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentFidelityTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentIndirectIoTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentInputContextTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentReferenceSearchTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentRulesetTests.cs`
- `tests/RoslynMcp.Tests/PackageHeldProofTests.cs`
- `tests/RoslynMcp.Tests/Services/WorkspaceCacheStoreInvalidationTests.cs`
- tests/RoslynMcp.Tests/CompilerReferenceContextGraphTests.cs
- tests/RoslynMcp.Tests/CompilerArgumentStageOwnershipTests.cs
- tests/RoslynMcp.Tests/NativeCompilerIdentityUnavailableTests.cs
- tests/RoslynMcp.Tests/NativeCompilerResponseReferenceTests.cs
- tests/RoslynMcp.Tests/PhysicalDirectoryFinalRemovalTests.cs
- tests/RoslynMcp.Tests/RestoreOutputEpochTests.cs
- tests/RoslynMcp.Tests/GeneratedEditorConfigSnapshotTests.cs
- tests/RoslynMcp.Tests/NativeDocumentDegradedAuthorityTests.cs
- tests/RoslynMcp.Tests/NativeFinalCompilerUnavailableTests.cs
- `tests/RoslynMcp.Tests/NativeCertificateCurrentGrantTests.cs`
- `tests/RoslynMcp.Tests/NativeCompilerContextCertificateTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowNamespaceAdmissionTests.cs`
- `tests/RoslynMcp.Tests/CompilerArgumentDirectoryOwnershipTests.cs`
- `tests/RoslynMcp.Tests/NativeTraversalRootTests.cs`
- `tests/RoslynMcp.Tests/RestoreCurrentReadAuthorityTests.cs`
- `tests/RoslynMcp.Tests/PhysicalInputMetadataAuthorityTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceCacheCurrentAuthorityTests.cs`
- `tests/RoslynMcp.Tests/UndoWriteAuthorizationOwnershipTests.cs`
- `tests/RoslynMcp.Tests/BuildPathAliasProvenanceTests.cs`
- `tests/RoslynMcp.Tests/CompositePreviewTruncationTests.cs`
- `tests/RoslynMcp.Tests/NativeCompilerInteropProducerTests.cs`
- `tests/RoslynMcp.Tests/PhysicalGraphStageTests.cs`
- `tests/RoslynMcp.Tests/NativeBuildMembershipTests.cs`
- `tests/RoslynMcp.Tests/NativeMembershipPublishedPropertyTests.cs`
- `tests/RoslynMcp.Tests/NativeOutputPreparationTests.cs`
- `tests/RoslynMcp.Tests/NativeOutputCoordinateTests.cs`
- `tests/RoslynMcp.Tests/TestRunnerEvaluationPropertiesTests.cs`
- `tests/RoslynMcp.Tests/TestPreparationRestoreBudgetTests.cs`
- `tests/RoslynMcp.Tests/ExplicitReloadAnalyzerLifetimeTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayFixture.cs`
- `tests/RoslynMcp.Tests/PreloadedAnalyzerAuthorityTests.cs`
- `tests/RoslynMcp.Tests/CommandStreamingContractTests.cs`
- `tests/RoslynMcp.Tests/ConcurrencyTrackerTests.cs`
- `tests/RoslynMcp.Tests/EvaluatedExecutionStringIdentityTests.cs`
- `tests/RoslynMcp.Tests/ImmutableInputAuthorityTests.cs`
- `tests/RoslynMcp.Tests/EvaluatedBuildGraphTests.cs`
- `tests/RoslynMcp.Tests/EvaluatedFrameworkContextTests.cs`
- `tests/RoslynMcp.Tests/DownloadedPackageAuthorityTests.cs`
- `tests/RoslynMcp.Tests/InstalledPackageIssuanceGrantTests.cs`
- `tests/RoslynMcp.Tests/NativeUsingTaskConditionTests.cs`
- `tests/RoslynMcp.Tests/NativeReferenceFreePublicationTests.cs`

## Acceptance

- [ ] Inventory every confirmed current setup, using/finally, permission/attribute/environment restoration and native process ownership defect; distinguish intentional cleanup-error controls.
- [ ] Reuse CleanupFailureCollector or OwnedResourceCleanup; retain the actual primary exception and attempt every independent owned cleanup step.
- [ ] Guard filesystem setup and ownership transfer; dispose untransferred results and avoid double ownership after successful transfer.
- [ ] Stop and join owned native children with exact started identity; preserve actual secondary-host and lifetime assertions.
- [ ] Observe the real issued parser stage cleanup; stale former-directory assertions cannot replace actual stage ownership proof.
- [ ] Preserve all assertions, input/path/alias controls, resource authority and original deadlines; qualify changed tests on current artifacts before closing.
- [ ] Close only after merged exact-head validation and complete current inventory reconciliation; historical candidate counts are not current defect counts.

## Evidence

Static scan found 368 finally/deletion candidates in 144 files, not 368 confirmed defects. Direct inspection confirms bare deletion in the anchored tests. NativeConditionSerializationTests two instances were corrected separately in the active prerequisite.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.

2026-10-22 cold review additionally observed tests/RoslynMcp.Tests/Services/WorkspaceCacheStoreInvalidationTests.cs:34-47 swallowing every teardown exception under an unsupported another-runner-lock claim. Include this confirmed cleanup shape in the existing inventory; preserve primary failure while surfacing cleanup failure through the established collector. This is separate from the current metadata/cache authority repair.

2026-10-23 cold source inspection additionally confirms MixedRestoreDocumentAuthorityTests.cs:16,36,48 bare fixture/candidate owners; preserve actual generated-props injection and native two-versus-one occurrence premises. Include NativeMtpReporterRegistrationTests.cs and NativeMtpExecutionTests.cs owned fixture setup/cleanup identified by full reader; scratch repairs pending cold review and native qualification.

2026-10-23 full source review confirms additional same cleanup mechanism: InstalledPackageAuthorityTests.cs fixture20/46/77/112/155, inline manager/watchers48/64, Adhoc84/blocker95/129, ActualSigned root174 and delete231; EvaluatedRestoreInputsTests.cs setup16/teardown19 and blocker181; PhysicalGraphAssetsOverlayTests.cs fixture18/98, manager/watchers50-51/103-104, stages60/110 and restoration finally80/124/141. Preserve actual package/overlay/native evaluation assertions while repairing lexical primary ownership. These are confirmed sites, not static candidate counts.

2026-10-23 additional full reads confirm NativeCompilerReferenceContextTests.cs:70 bare JsonDocument; NativeCompilerTaskContextTests.cs:22-23 mkdir before protected setup; NativeDependencyGrantTests.cs:16/56 fixture,25/60 manager and Manager(policy):83-85 inline watcher constructor-failure custody. Preserve original native assertions.

2026-10-23 further confirmed cold full reads: RestoreDestinationAuthorityTests mkdir18/finally38; RestoreBoundaryRegressionTests setup36-47/64-70/88-89/108-116 and finally54/78/99/129-132 (preserve exact original attrs); RestoredPackageManifestTests WriteAllBytes174 beforetry/delete182, installation81 and JsonDocuments41/69/106/130/133/147/158/168/CTS169. NativeConditionCacheObservationTests observer39/67/86/197/233; VerifyUnregisteredTarget setup128-131 and finally182-183; VerifyReplacement247-269 no finally for poisoned observer/cache. Preserve deliberate observed retirement failure and all256/15s native tier/JIT controls.

2026-10-23 SupplementalPackageReadAuthorityTests cleanup sites: fixture25/86, manager29/100 inline watcher factory193, unrelated41, scope130/owned131/candidate166. Correct actual missing-private invocation separately under supplemental-package-fixture-invocation-authority; primary cleanup must retain same scope/authority order.

2026-10-23 additional confirmed sites: NativeConditionElementBindingTests evidence21; NativeConditionSerializationTests mkdir26/79 and native owner33/independent116/evidence132; RetainedPackageReplayAuthorityTests fixture16/candidates21/31, restoration finally65/76 and mkdir84/delete96; RestorePathSlotNativeTests keep-fork created83 has separate namespace not deleted by current finally126-129. Preserve deliberate native retirement failure controls and actual retained fork verification; delete only exact owned fork after sessions close.

2026-10-23 further full-read inventory: PhysicalDirectoryCleanupOutcomeTests NewFixture123 mkdir before callertry, setup45-46/69 and scopes18/50/75-78/89-90/109-110; PhysicalDirectoryRetryAuthorityTests mkdir21/55/57/84-90/123, barefinally44/73-74/112/153, owned24/126. Preserve actual no-delete/rename/POSIX retained/replacement outcomes and logger-reached premises. Helpers NativeConditionProcessTest.cs results mkdir36 beforetry39; ResolvedTestPackageFeed.cs JsonDocument19. Move setup into existing ownership and use actual primary cleanup, retaining exact finite60s child control.

2026-10-23 additional source-qualified cleanup inventory: WorkspaceSessionLoaderAdmissionTests CTS68; NativeMembershipEncodingTests ProjectCollection28/CTS59; NativeTaskContextEncodingTests ProjectCollection23/CTS35; NativeMembershipPropertyTests setup30, sequential envrestore72/delete73 and ProjectCollections83/100. NativeReferenceCoordinateOracle helper nested PEReader(File.OpenRead(validAt))99 and consumer222 has constructor-transfer/primary custody gap; preserve real pathname/PE oracle using existing ownership or immutable bytes. ProjectCollection236 potential-gap report WITHDRAWN: only nonfallible locals before owning try.

2026-10-23 WindowsDirectoryCoordinateAuthorityTests full source review confirms WithRoot mkdir175 before owningtry177 and native handles/scopes/readhandles using24-26/38-39/54/73-76/103-106/113/115/135/158-159. Preserve actual directWin32/managed/held identity, literal-vs-normalized sibling, actual parent pin and metadata timestamp controls across all14 rows.

2026-10-23 full source review additionally confirms PhysicalDirectoryAcquisitionAuthorityTests WindowsPhysicalRoute138/146 rawusing and ReplacingParent.CreateOwnedChildScope catch183 raw created.Dispose masking original callback/acquisition failure. PhysicalGraphContentReplayTests fixture24/87, inline manager/watchers43-44/122-123, stages56/138 and byte restoration finally175; PreparedUndoCallerAuthorityTests all8 fixtures19/41/69/103/129/149/187 awaitusing. Preserve real Windows pin/POSIX replacement, partial effects/recovery, refusal markers and native content/replay/assertions.

Path correction from live file discovery: prior PreparedUndoCallerAuthorityTests.cs note refers to tests/RoslynMcp.Tests/PreparedUndoCallerTests.cs (all8 same cited fixtures); NativeReferenceCoordinateOracle helper note refers to tests/RoslynMcp.Tests/NativeReferenceCoordinateOracleTests.cs containing those nested PEReader transfers. Use these exact existing paths; prior names are superseded.

2026-10-23 full read confirms Workspace/WorkspaceRecoveryConflictTests.cs629 bare Undo89/116/139/168, leases220/284/407/460/592, CTS416/601, FileStream554, setup541 beforetry543, inline manager/watchers248/376. WorkspaceUndoOwnershipTests.cs413 source36 setup beforeguard68/Undo60/CTS65, source100/Undo105, source127/manager129/Undo131, source160/manager162/Undo164 and sequentialfinally209-219; remaining lease/source/Undo238-404. Preserve deliberate lease retirement before recovery/publication assertions and attempt every exact independent restore/Abort/Dispose/Revert/Close. LocalPackageForkTests MemoryStream221 same primary-custody inventory.

2026-10-23 full source reads: NativeOutputProducerContextTests mkdir17/ObservationTests24; NativeWriterReceiptAuthorityTests mkdir25+CTS48, NativeWriterFactoryAuthorityTests15, NativeTaskSelectionTests17 (use exact live file paths when reconciling). WorkspaceNativeCleanupFailureTests root313/PE stream324, watcher376 unconditional duplicate manager ownership, ObservingLoader495 stores successfully transferred loads and releases again; move to single transfer/fallback, preserve deliberate failure injection. MissingRoot byte restoration176/246 and retirement WaitAsync10s must release barrier and independently join despite primary failure before dependent cleanup. OriginalNameBindingsTests19/OriginalNamespaceScopeTests18/OriginalTypeShapeTests21/111/142 Adhoc bareusing.

2026-10-23 full source reads add NativePathCoordinateTests five sync root-beforetry/baredelete helpers and native collection/manager owners; NativeReferenceDiagnosticTests all6 Adhoc/MemoryStream owners. TestRunOutputReaderTests setup77/108/139, DotnetOutputParserTrxIdentityTests Parse mkdir/delete, QueuedCommandAuthorityTests setup20-36 includes executor/blocker before owningtry. PhysicalPathResolverFreshnessTests4 setup/delete, PhysicalPathHandleFreshnessTests2 setup/delete+WindowsDriveAlias57, FileContentDigestTests WithRoot145 mkdir/physicalresolve pretry +blocker/CTS owners; PhysicalInputBatchIntegrationTests mkdir17/51-52+CTS82. Preserve actual actors/token/noinline/surrogate/literal/metadata/count/time predicates; correct exact names by discovery before application.

2026-10-23 scope correction: plain deterministic MemoryStream alone (LocalPackageFork and NativeReferenceDiagnostic) or CTS alone is not evidence of throwing cleanup and must not force unrelated churn. Preserve substantive native/Adhoc/FS/task ownership findings. Added actual NativeTaskExecutionUnavailableTests root20/NativeTaskRegistrationsTests28/native collections44/60/NativeTaskRegistryUnavailableTests20/workspace42+lease47/NativeToolsVersionTests18; retire workspace before analyzerlease. NativeDeclarationOrdinalTests setup24/delete67/evidence32 and NativeReferenceAliasTests Adhoc41. Recheck exact discovered filenames before edits; no coding from abbreviated names.

2026-10-23 latest full/read diff inventory: WorkspacePathPolicyTests trailing separator setup49/delete55 and linked ancestor setup137-139/delete159; actual MCP-root servers112/114 awaitusing. NativeOutputPreparationPathTests rootmkdir beforeguard; BuildPathRolesLiteralExistsTests mkdir/delete/nativecollection; NativeMandatorySourceAuthorityTests fixture26/manager43 inlinewatcher; NativeUserExtensionIsolationTests rawfinally48 and owners52/65/72/83, maintain stageauthoritythroughcandidate/stagecleanup. ParameterObjectPreviewTests exact missed multi-project direct loads1099/1153/1195/1430/2234 setup-beforeguard/rawClosefinally and straywrite2276 beforeprotectedownerbody: migrate allsame omitted actualscopeownership, preserving native binding/host redemption/refusal controls. Full2624file read notclaimed; exactHEADdiff coverage independently completed.

Continuation27 cold-review inventory (source25 frozen; not repaired/qualified): CompilerArgumentNativeProjectReferenceTests197 three mkdir-before-guard and two raw delete finallys; RestoredPackageManifestTests188 temporary write before try/raw File.Delete and native fixture ownership; RetainedPackageReplayAuthorityTests107 native fixture/candidate leases plus serial byte restoration/delete. Helpers/NativeItemCoordinateOracle.cs58 mkdir+Resolve before try needs exact lexical fallback. PerformanceBehaviorTests228 held reader/second task and validation execution need release/cancel+join before gate/executor/cache retirement while preserving 5s/10s assertions; Workspace_Status serial close/delete masks primary. PreviewStore six plain AdhocWorkspace owners require disposal without invented native IO severity. UndoFileOperations first two native loads pretry and serial close/delete; new persistence two await-using native scopes; UndoService prepared encoding mutation/workspace lease cleanup must preserve body failure. RestoreCurrentReadAuthority content-lock before-read control needs independently observed Windows read refusal; metadata-polling control cannot claim content-sharing denial proves metadata order. Keep exact current refusal as separately scoped control. No product defect inferred from fixture strength gap.

Continuation27 additional source-ready SCRATCH ONLY (not applied/nativequalified): root PerformanceBehaviorTests-cleanup27.cs six assertions retained; release/cancel-incomplete/join all startedtasks before owned gate/cache/executor retirement, CTS last, original5s/10s limits. Root WorkspacePathPolicyTests-cleanup27.cs22assertions retained; actualpartialmkdir/linkrootcleanup andtwoMCPsession primary/LIFO independentdisposal; pureRequestContext/Revokeunchanged. Plan directory-test-cleanup fivefiles PhysicalDirectoryCleanupOutcome/Retry/Acquisition, PhysicalGraphParentOwnership, WindowsDirectoryCoordinate:24/23/25/5/35assertions retained,30ASTscopes, deliberateearlyWindows pin/POSIXretirement unchanged, wholecoldsourceReady. Allsource25 frozen, currentRuntimepending, nocompletionclaim.

Continuation27 native fixture helper additional exact same mechanism: Helpers/NuGetInstallationFixture.cs48–60 uses OwnedResourceCleanup.Dispose(primary) in failed creation, wrapping primary; WriteArchiveAsync raw await-using FileStream/ZipArchive/entry can mask write failure. Existing guarded root setup and2min owned producer are correct and must remain. Plan author widening scratch cluster only after independent coldplan; no helper fix/current native qualification claimed.

Continuation27 parent full semantic reread calibrates earlier inventory: OriginalNameBindingsTests90, OriginalNamespaceScopeTests59 and OriginalTypeShapeTests161 already use lexical using for allfive pure in-memory Adhoc workspaces; NativeReferenceAliasIdentityTests75 (correct actual filename, not NativeReferenceAliasTests) also already uses lexical Adhoc owner and never creates its root. No demonstrated throwing native cleanup here; withdraw mandatory collector rewrites. PreviewStoreTests344 has FIVE plain workspace variables88/106/120/134/157 followed by body-end Dispose (not six wholly unretired); protect failure paths with lexical using and remove five trailing Dispose only. NativeDeclarationOrdinalTests69 retains actual native observer/root partialsetup and primarycleanup finding.

Current24 changed24classes completed213=206P6F1skip1h2m. Exact6failures are oldReferenceFree4 invalid-untriggeredactors fixedcurrent25 and nativeEdge2premises; no new cleanup regression fromthat run. Current25 conditionclasses25P. Rootapplied66sourceReadytests-onlyfiles afteroriginal/draftSHAchecks incl3NEWold-compatible regressionclasses, LocalPackage staticfactorycompilecorrection+takeclearretirement corrected/coldread; native26OLD buildPASS0warnings/errors14.55 and all1381posthashmatch. Newoldcausalrun38748active; no kind/parserproductionpatch applied. RawframeworkTestCleanup maskingclaimwithdrawn withoutactualframeworksource/runtimeproof; pureOriginalAdhoc/aliascollectorwithdrawalsretained.

Additional same-mechanism read: tests/GitFixtureWorkerProbe/Program.cs:58-63 writes the release file in raw finally before restoring ThreadPool and joining the actual worker. A release-write failure can mask the primary result and skip the join. Correct this owned child-probe cleanup sequence with independent primary-preserving release/restoration/join ownership; this is separate from environment isolation because the probe has its own process. No fix or runtime proof yet.
