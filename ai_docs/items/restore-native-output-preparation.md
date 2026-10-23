# restore-native-output-preparation — Bound native output preparation before final compiler authority

**row:** `restore-native-output-preparation` · **pri:** `High` · **size:** `L` · **deps:** `native-transitive-compiler-graph`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerTaskInputs.cs`
- `src/RoslynMcp.Roslyn/Services/NativeOutputPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/BuildPathRoles.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedExecutionIdentity.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs`
- `tests/RoslynMcp.Tests/NativeOutputPreparationTests.cs`
- `tests/RoslynMcp.Tests/PhysicalGraphStageTests.cs`
- `ai_docs/plans/20261016-native-output-preparation.md`

## Acceptance

- [ ] Execute one private bounded preparation phase, then recapture current authorized inputs and load a fresh final native workspace; never retry changing native output until it is accepted.
- [ ] Preserve frozen authored/import/config/package/restore identities and exact physical target identity; permit only exact declared producer outputs, without obj subtree or symlink target waivers.
- [ ] Compare final native options/reference contexts and required actual edge-normalization producer receipts against the final strict epoch; provisional observations issue no public compiler, image or parser authority.
- [ ] Preserve no-PackageReference missing assets/dgspec and exact broken-state physical replay; do not restore/prebuild those fixtures or conflate missing restore state with verified package authority.
- [ ] Preserve cancellation, current path policy, diagnostic isolation, old-session atomicity and complete owned workspace/child/channel cleanup.
- [ ] Verify the forced companion in the active restore initiative and close only after complete verified merge.

## Evidence

| Check | Current-session observation |
|---|---|
| Original preparation6 | Four failures/two passes: fresh no-edge, missing assets/dgspec2 and restored edge reject legitimate output production; actual authored mutation and continually changing output controls refuse. |
| Initial implementation | Five own cases pass; restored edge fails prepared Missing-to-Directory classification. No-PackageReference restore artifacts remain absent. |
| Bounded ancestry | All five path controls pass: necessary declared parent accepted, sibling/file rejected, existing/new link components rejected before hashing. |
| Parent-link premise correction | Original hypothesis was invalid: existing policy and EnsurePhysicalPath already reject parent filesystem links before hashing. No resolver change or product-gap claim; tests assert established rejection. |
| Introduced output inputs | Restored edge advances to recapture and refuses Dependency.AssemblyInfoInputs.cache, Dependency.assets.cache and Dependency.GeneratedMSBuildEditorConfig.editorconfig. All are new mutable files and final inputs, with no original exact declared output role. No wildcard/cache/obj-subtree waiver is authorized. |
| Source-reference classification | Source-role correction verifies7 native reference controls,4 strict metadata search controls and both actual reactivation cases. Missing outputs retain authorized physical absence; unmatched metadata stays strict. |
| Public relay capability | Executed native writer exists, but public ILogger relay exposes Source3=False, Source4=False and zero targets/tasks/parameters. This route cannot issue producer receipts; no waiver or DLL-copy workaround. |
| Owned producer replacement | Shared existing child/channel captures successful writer facts before provisional Open in all original inner contexts. Original target definition, exact SDK assembly hashes, typed output parameter, declared output scope, current policy and protected roles bind output bytes through final recapture. Final Csc success requirements remain strict. |
| Focused gate1321 | Own preparation6, path5, native source-reference7, metadata search4 and actual owned writer1 ALLPASS. Combined45 is39pass/6fail: graph fixture positives and real analyzer lifecycle/image cleanup remain red with their owners. No aggregate-green claim. |
| Additional controls | Receipt authority9 and actual lowercase-task writer control installed tests-only; source case classifier unchanged pending causal runtime. Wrong expected assembly hash is synthetic; no SDK file is mutated. |
| Status | Implementation and verification remain incomplete; no full gate, PR or merged completion claim. |

## Context

Forced companion of the operator-authorized restore initiative. Exact native admission must distinguish legitimate first-load production from authored/dependency mutation without weakening snapshots or pre-restoring missing-state fixtures.

Authoritative protocol and evidence: `ai_docs/plans/20261016-native-output-preparation.md`. Parent serializes runtime gates; output observations alone grant no authority.

| Factory boundary | Current evidence / obligation |
|---|---|
| Valid causal | restore-factory-causal2 executes inline RoslynCodeTaskFactory Override=true WriteLinesToFile and writes the marker. Trusted Microsoft.Build.Tasks.Core.dll identifies the FACTORY; current admission wrongly grants one builtin writer role. |
| Invalid initial premise | Compile-dependent fixture failed before marker. Explicit original OwnedFactoryProbe target supersedes it; no causal claim from the invalid fixture. |
| Complete fix | Require actual native active registration/implementation proof and original/child context correlation. Native custom task execution remains allowed; no generic task ban or legitimate-overwrite defect claim. |
| Pending capability | Original-registry snapshot and same-project/different-global tests installed, unrun. Current/floor ABI proof and precise typed unavailable semantic-load behavior remain mandatory. |
| Status | No SDK file mutation or initiative completion claim. |

## Exact task/context identity and semantic-only capability evidence (2026-10-16)

| Mechanism | Current-session evidence / disposition |
|---|---|
| Created-wrapper selection | Actual builtin x64 control passes; actual x86 custom factory executes and historical builtin-only selection wrongly admits one output. Preserve every matching created custom/differing-host candidate as uncertainty; ignore uninstantiated registrations. |
| Dependency-sensitive uncertainty | Mixed task history introduces an actual AdditionalFiles input and previously aborts semantic loading as undeclared. Semantic-input, irrelevant-output and authored-mutation controls now all pass; unavailable execution context grants no compiler-image or physical mutation authority. |
| Fully qualified Csc | Actual nonempty CommandLineArgs premise establishes two wrong zero-task/boundary outcomes on old short-only dispatch. Shared short/full classifier fixes both; four direct/nested cases pass. |
| Exact UTF16 fingerprint | Old native context hashes collapse three escaped surrogate/replacement pairs; analyzer catalog hashes collapse two pairs. Shared length-plus-ushort framing preserves exact code units and required cancellation; no raw values logged. |
| Shared scope | NativeTaskExecutionIdentity, NativeTaskRegistrations, AnalyzerInputCatalog and ruleset staging-coordinate hash use NativeIdentityEncoding. Preserve native ruleset lowercase-cycle semantics and case-distinct physical suffixes; do not alter actual byte-content hashes. |
| Fresh verification | restore-exact-encoding-fixed1/encoding-csc-ruleset.trx:19 passed,0 failed,0 skipped; encoding8, Csc4 and complete ruleset7. No full initiative or publication claim. |
| Anchors | src/RoslynMcp.Roslyn/Services/NativeIdentityEncoding.cs; NativeWriterOutputScope.cs; NativeCompilerContextCertificate.cs; tests/RoslynMcp.Tests/NativeTaskContextEncodingTests.cs; AnalyzerContextEncodingTests.cs; NativeTaskExecutionUnavailableTests.cs; NativeCompilerTaskContextTests.cs. |
| Remaining | Typed compiler/checked-ABI uncertainty, required original effective ToolsVersion binding, exact-head complete gates, independent review and merge remain open. |

### 2026-10-16: exact context, private transport and supplemental semantic-read evidence

| Mechanism | Current-session evidence | Status |
|---|---|---|
| Exact private IPC | Old actual frame5 loses unpaired high/low UTF16 values; replacement/pair/ordinary3 pass. Shared exact padded Base64URL codec, logical compatibility quotas and finite5x wire limits pass fresh50; necessary per-token/cardinality preflight plus existing quota controls pass12. | OPEN; no broad persisted/public serialization change. |
| Effective ToolsVersion | Original native instance value is captured and hashed in task-registry witness; required producer/certificate APIs carry it and child event/configuration/effective-instance identities must agree. Default and legacy-project normalize to Current; explicit nested15.0 fails independent native MSB4132, not an owner mismatch. | Native toolset3 plus context encoding7 pass within fresh25-positive gate. |
| Original package semantic-read transfer | Manager issues exact source-session/current-Solution/version-bound proof; selecting assets/package hashes/current original-scope grants are rechecked across native boundaries and immediately before publication. Missing private assets remain missing and unavailable for compiler/image/physical authority. | Package7 pass; no synthetic assets or fork-scope package grant. |
| Missing authored PackageReference | Source restore/assets/imported Compile document remain real after authored reference removal before source loading. Old private missing-assets load fails original cache-grant scope; declared-reference control passes. | Missing-assets condition now also recognizes nonempty supplemental exact Files; fresh verification pending. |
| Exact package file membership | MandatoryDocuments separates directory tool roots from exact verified-file membership and retains authored-root precedence. | Exact/descendant/authored/unverified4 pass; no physical descendant escalation was demonstrated. |
| Final authoritative compiler uncertainty | Preparation uncertainty already retains semantic load; final Normalize still propagates the dedicated uncertainty cause. Mutex-controlled actual phase-specific native test fixture is being corrected; earlier tests failed prerequisites and establish no causal final-boundary result. | OPEN; final fallback implementation held until valid native premise. |
| Normalization check placement | Initial snapshot/grant check and every post-child check retained; redundant pre-child checks removed where only immutable work intervenes. Final bulk check moved after parsing-receipt IO and before proof publication. | Source-reviewed; fresh latency/correctness pending; preparation assertions unchanged. |

Keep this row OPEN until complete exact-head validation, review and merge. Full native fork validation and extraction's admitted-image production bridge remain pending dependencies; focused component results do not establish initiative completion.

2026-10-23 source admission positive genuine failure (old22 andcurrent23): NativeSourceAdmission.IssueAsync retains captured Missing extras outside initial epoch Inputs; safe hash diagnostics map actual transitions to .NETCoreApp,Version=v10.0.AssemblyAttributes.cs, Native.AssemblyInfo.cs, Native.GeneratedMSBuildEditorConfig.editorconfig under actual obj/Debug/net10.0. No obj/generated-role exclusion allowed. New tests-only NativeOutputPreparationTests.MissingGeneratedInputs_RecordActualPreparationAndNativeOpenProducerPhases records exact3 before/after explicitPrepare/actualOpen and task/context/implementation/output witnesses before receipt verdict. Native25build queued87445; actual producer phase/WriteCodeFragment attribution and immutable bound before/after transition receipt remain unqualified. Replace only verified initial-chain slots with admitted after identities in final closure; keep other extras/intents frozen.

Continuation27 SourceAdmission transition design corrected after independent challenge: Missing-only admission restriction is NOT existing writer contract. NativeWriterOutputScope.TryGetPath26–35 protects actual input roles +declared output coordinate, and Producers.Admit44–50 admits verified successful File outputs without Missing-before requirement. Exact captured-before must be in actual perproducer final complete grant+callbackfree beforeproof, including extra captured slots absent initial.Inputs; existing later callbacks after AssertInitial can otherwise mutate/adopt extras. Accept supported before→after transitions only through exact issued successful writer/context/implementation and protected-role/provenance proof; retain accepted AFTER identities in final closure even absent finalInputs. Existing authored/opaque/input roles frozen by actual roles, not File kind. No obj/prefix exemption, forged receipt or assumed WriteCodeFragment/Open-only support. Actual25phase inventory pending; no product fix/runtime claim.

Current immutable25 actual inventory2sFAIL exact WriteCodeFragment receipt expected1actual0 AFTER allthree originalMissing coordinates independently proved File afterPreparation and afterOpen; FirstPhasePreparation allthree. Recognized events WriteLinesToFile/GenerateMSBuildEditorConfig only, so notyetactualCodeFragment taskattribution. Observation-first extendactualnativeevent facts WITHOUT outputadmissionmaps, then requirequalified actualimpl/assembly/context/OutputFile/success beforeadmission. No sourceAdmissionrole exemption.

Actual source28 StageA inventory qualified WriteCodeFragment SDK Core assembly/builtin implementation/OutputFile/CoreGenerateAssemblyInfo/exact effective context. All 12 observation/factory/context controls passed; original-context admission case failed expected1actual0 before support. Independent cold review approved exact two admission-map additions; root applied both with matching before/draft hashes, scoped Roslyn zero warnings/errors. Native current receipt/context/factory/cached-host/hash/current-policy guards remain pending. This recognition does not solve SourceAdmission callback witness, captured extra transitions, ordered producer lineage or exact final-epoch/context association.

New fixture expected-context defect confirmed across NativeOutputProducerContextTests, NativeOutputProducerObservationTests, NativeOutputReceiptAuthorityTests and NativeOutputTaskSelectionTests: EvaluatedBuildGraph stores escaped globals and EffectiveGlobals preserves them, so expected identity must use EscapedContextIdentity like actual observer and scope. Ordinary paths masked the incorrect ContextIdentity call. Correct all four expected-value sites and qualify a nonpath escaped caller property in the cached-selection native matrix before shipping; no production guard change.

Root applied all four independently reviewed expected-context/cached-host fixture changes with matching hashes; scoped Roslyn zero warnings/errors. Native source29 build passed zero warnings/errors in14.07s; all1381 source identities matched postbuild. Current focused native matrix is running, including exact writer/context/factory/receipt/cache/phase tests and environment isolation controls. No current runtime totals or full-CI/merge claim yet.

Completed source29 current writer controls: observation4/context4/taskfactory4/receiptauthority18/cachedhost4 all passed, actual preparation11passed1knownEditorConfigtransitionfailed; executionUnavailable2passed1finalcompilerfixturefailed. Total124=122passed2failed0skipped3m12, raw causal29.trx. CodeFragment original-context admission now passes after observed old-red; escaped nonpath caller property is independently native-qualified. Same-context/inline-factory/implementation/hash/current-policy/protected controls retained. SourceAdmission before/after/issuer fix remains incomplete; native fixture final-compiler premises under investigation, not a product regression or baseline/environment attribution.

2026-10-23 fresh cold derivation: source29 generated-trio failure is retained Missing identities at NativeSourceAdmission.IssueAsync. Static EvaluatedBuildGraph.DocumentFiles is evaluated item membership, not semantic membership; retain full static protection. Required repair needs role-tagged parser observations, actual native generated enrollment evidence, immediate producer before/after identities, ordered transitions, and exact original/final/context/native-Solution issuer linkage. Concrete scratch role plan: <user-profile>/.codex/scratch/roslyn-remediate-20261023/source-transition-role-plan.md. No permissive transition is implemented or approved. Current-session immutable NativeTaskExecutionUnavailableTests reproduces 2 passed/1 failed; bounded first diagnostic is inconclusive, no unchanged retry or fixture-success claim.

Current-session diagnostic now proves MSB4275 in all three unavailable fixture rows. Both NativeTaskExecutionUnavailableTests and NativeOutputTaskSelectionTests declare two Override=true registrations per task; WriteCodeFragment replacement inherits the same malformed shape. Other three single-override fixtures remain legal. Preparation deliberately tolerates failed producer exit, so successful writer/body receipts alone falsely qualified selection controls. Required fix: preserve actual original child outcome as internal evidence, assert original success for success controls and explicit false for intentionally failing post-writer control, then remove both duplicate overrides while retaining architectures/factories/assertions. Temporary diagnostic production sources were restored byte-exact; baseline2P1F and diagnostic2P1F TRX retained in scratch20261023.
