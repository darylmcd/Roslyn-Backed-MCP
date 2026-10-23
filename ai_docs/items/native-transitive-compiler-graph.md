# native-transitive-compiler-graph — Reviewed edits must predict the actual compiler reference closure

**row:** `native-transitive-compiler-graph` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs`
- `src/RoslynMcp.Roslyn/Services/BuildPathRoles.cs`
- `tests/RoslynMcp.Tests/BuildPathDeclarationEqualityTests.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerTaskInputs.cs`
- `src/RoslynMcp.Roslyn/Services/CompilerReferenceCaptureLogger.cs`
- `src/RoslynMcp.Roslyn/Services/RestoredCompilerInputAuthority.cs`
- `tests/RoslynMcp.Tests/NativeCompilerInteropProducerTests.cs`
- `tests/RoslynMcp.Tests/NativeCompilerContextCertificateTests.cs`
- `tests/RoslynMcp.Tests/NativeCompilerReferenceContextTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`
- `src/RoslynMcp.Roslyn/Services/CompilerReferenceContextGraph.cs`
- `tests/RoslynMcp.Tests/CompilerReferenceContextGraphTests.cs`

## Acceptance

- [ ] Predict the exact native compiler closure after reviewed project edits, including SDK transitive edges and framework/context selection.
- [ ] Bind authority to an owned native load and approved input epoch; caller-created previews cannot issue certificates.
- [ ] Observe trusted Csc task-input reference metadata independently; authored/default EmbedInteropTypes cannot substitute for executed targets.
- [ ] Compare actual Csc arguments, compiler options, source/additional/config inputs, output and reference identities with the native original; preserve aliases and context identity.
- [ ] Keep compiler arguments and task values in bounded private memory-only IPC; drain child output without exposing native messages or values.
- [ ] Bind authored definitions, source, HintPath/analyzer artifacts, condition inputs, restore assets and SDK policy to approved physical/hash identities before and after additional execution.
- [ ] Carry independently issued edge metadata through planning, staged validation and final reload; never graft unreviewed candidate references into expectations.
- [ ] Preserve source bytes and recovery authority; retain direct/generated target overrides, post-target item poisoning, drift and forged-snapshot controls.
- [ ] Keep degraded semantic loading for invalid restore assets; expose actionable known-root repair guidance and require a repaired reload before mutation certification.

- [ ] Compare active declaration evidence by all scalar values and ordered operand contents, including case, suffix and missing-inventory state; independently evaluated equal arrays must hash and deduplicate equally.

## Evidence

- `restore-authority-contexts6.log`: actual native reload adds the reviewed SampleLib-to-Contracts transitive edge absent from the prior expectation.
- `restore-compiler-authority-native1.log`: 7 pass, 4 fail; generated interop metadata and old implicit lineage require complete propagation.
- `restore-direct-framework-context-old1.log`: actual native framework diamond establishes two Contract contexts; prior authority falsely reports ambiguity.
- `restore-context-condition-producer2.log`: actual default and generated-target override producer controls pass; the broader run remains red on condition collection.
- `restore-condition-native-fixed2.log`: current production and tests compile; 47 pass, 14 fail, with SDK path cases blocked by condition provenance before producer execution.
- Additional producer, artifact drift, degraded-manager repair and metadata propagation changes still require focused and full validation.

- `restore-analyzer-lifecycle-identity-diagnostic2.log`: all three native lifecycle failures reached duplicate build identity; fixed-label diagnostic isolated `Declarations`. Independent-array equality positive failed while all 12 changed-intent controls passed. Canonical declaration equality/hash correction is source-only until the next fresh focused gate; both capture `Distinct` and graph `HashSet` comparisons use the value contract.

## Context

- Complete-fix companion exposed by the operator-authorized restore initiative; close only with verified, merged implementation.
- Scope is L because one authority contract spans native loading, independent compiler execution, graph expectations and recovery; file targets do not justify a partial fix.

Required input closure: authorize compiler response files before own-process parsing, bind nested response content to the same immutable epoch, and retain actual response-file authorization/drift controls. Scoped cold review identified this new producer gap; fix in this initiative before shipping, with no response-file ban or deferred defect.

Current continuation evidence: parser/ruleset/fidelity21 passed; later actual SelectiveTaskChannel4, response-reference2 and nested cleanup2 passed after correcting fixture tool-root provenance and required runtime attribute assemblies. Causal localized NoMatch variant failed4/7 and nested NoConfig variant failed1/7, with exact production hash restoration. Lib trailing-separator variant passed7/7 and establishes no defect; remove unsupported fix claim. Parent review also exposed repeated exact-solution issuance silently retaining the first parsing receipt through ConditionalWeakTable.GetValue; one issued projection per certificate with current-policy/receipt revalidation is implemented source-only and still needs complete native-context runtime proof. No full gate, PR, merge or completion claim.

## Native source-reference and alias checkpoint (2026-10-16)

| Mechanism | Current-session evidence / status |
|---|---|
| Unbuilt source edge | Guarded native diagnostic: Y.dll actual task input has SourceProject, original identity, one exact independently loaded target and one parsed operand; all frozen physical candidates Missing. Treating that selected CompilationReference as a built metadata file wrongly refuses valid reactivation. |
| Source-role correction | Required explicit native-project reference population is derived from actual task inputs plus independently loaded project edges. Only an exact absolute operand/path/Ordinal aliases/interop/image-kind match uses the source role; current authorization and frozen physical kind/content/absence remain required. Unmatched metadata retains official frozen search resolution. Fresh runtime pending. |
| Alias comparison defect | Prior shared reference key compared concatenated path and CSharp aliases with host filesystem comparer, folding aliases on Windows. Structured identity now uses host path comparison and Ordinal alias comparison in task, parsed and native metadata comparisons. |
| Alias causal | native-reference-alias-localized-old: one historical comparer substitution fails actual Alias versus alias equality assertion. Exact source SHA256 original/restored29B6F62A8AE1017E1B9DAF40207446997780F1EE6D04F08930B399C352BE3DB0; no restoration error. Fresh fixed gate active. |
| Validation limit | First combined build compiled but test discovery failed before execution: test-local ILogger forced MSBuild.Framework loading before initializer. Moved actual shared observer into the production library and instantiate after initialization; current combined gate remains active. No source-reference runtime pass, full gate or merged claim. |

## Validated duplicate compiler contexts (2026-10-16)

| Mechanism | Current-session evidence / status |
|---|---|
| Raw duplicate admission | `restore-compiler-context-diagnostic1.log`: three actual native lifecycle failures contain four original instances; two SampleLib instances have the same physical path and all ten global names/values match. Per-declaration graph interpretation ran before the outer node consistency check and incorrectly required a unique raw instance. |
| Causal fixture | The seven-row `CompilerReferenceContextGraphTests` old-source gate reproduced the equivalent-duplicate refusal. Six inconsistent property/item/default/reference/target controls also refused, wrapped by the nested MSBuild aggregate; no inconsistent duplicate was admitted. |
| Complete consistency proof | Required `ValidatedContexts` is created once before per-declaration graph interpretation. Shared path/global identity comparison groups candidates; full evaluated properties, in-memory public item-definition/item/target/toolset/default/initial snapshot, ordered imports and independently captured path evidence must match before canonicalization. Preserve the outer node/dependency check. |
| Parallel callback ownership | Replace the unsynchronized interpretation-to-original Dictionary with ConcurrentDictionary; graph callbacks execute through MSBuild ParallelWorkSet. Exact duplicate registration still refuses. |
| Shared evidence contract | Extract `Evidence.EquivalentTo` and reuse it in both duplicate-instance admission and outer node validation. Preserve every coordinate/file/declaration field, including native condition raw QueryPath and ObservedExists. |
| New controls | Sixteen source-coherent rows cover independently evaluated equivalent contexts, all evaluated-state mutations above, every path-evidence member, and separate condition query/truth changes. Actual fresh sixteen-row and lifecycle runtime remain pending; no full-gate, merge or completion claim. |

Validated target projection also deduplicates the already-proven canonical ProjectInstance objects by reference identity BEFORE constructing CompilerProjectReferenceTarget records. The previous post-projection Distinct compared newly allocated Globals dictionaries by backing identity; no name-only context collapse or generic record-equality workaround remains. Other actual consumers use explicit metadata comparison or serialized value comparison. Fresh runtime remains pending.

## Duplicate-context focused result (2026-10-16)

| Evidence | Result / limits |
|---|---|
| `restore-graph-factory-causal1.trx` | All 18 CompilerReferenceContextGraphTests passed in the 19-case combined run (6.63s); the separate task-factory fixture failed before its intended boundary because its marker was not created. This is graph-control evidence only. |
| Positive native fixture | Physical ProjectInstance(path) loads with separate private collections establish exact fixture Directory and ProjectReference FullPath before validation. Equivalent duplicate and Alpha/Beta insertion-order positives pass; no RawXml property-order defect was reproduced. |
| Negative consistency matrix | Sixteen changed evaluated-state/path-evidence controls refuse before nested graph interpretation, including raw query spelling and observed truth. |
| Publication status | Row remains open. Original active UsingTask registration witness is a newly exposed required consistency input: ToProjectRootElement does not include task-registration state. Implement with the same original evaluation before project unload; never re-evaluate property functions. No full aggregate or merged claim. |

2026-10-16 exact string identity follow-on: parent actual OLD graph12 produced10FAIL/2samePASS for programmatic captured UTF16 global values/keys, nested compiler aliases/target keys, restore strings/package IDs. Shared exact length+ushort identity serializer now encodes string values AND dictionary keys with canonical dictionary ordering; fixed graph12 plus shared codec/malformed/order/cancellation12 and duplicate-context19 ALL passed in graph-fixed-ipc-key-old1 (46pass4expectedfail50). The two remaining physical project/output key OLD controls validly collided; those keys now use same shared serializer, fresh validation pending. Captured invalid XML acceptance is not claimed. Diagnostic component labels remain fixed and captured encoded names are compared ordinal. No current-head aggregate or closure claim.

## Exact reference and comparer identities (2026-10-16)

| Mechanism | Current-session causal / correction | Verification |
|---|---|---|
| Alias delimiter ambiguity | OLD alias34 exposes8 failures: normalized projection split/join2, native key split1 and planner identity families5. Replace joined aliases with typed ProjectId/Ordinal alias sequences, immutable native reference comparer/hash, and exact structured planner suffixes; preserve deliberate EmbedInterop normalization. | All34 pass fresh Windows and the same exact DLL on Linux SDK10.0.400. |
| Physical-key consumer comparer | OLD Windows source/output2 collapse normalized @ versus Z paths because Base64URL keys are compared with FileSystemPath.Comparer. PhysicalProjectIdentity.Key now emits uppercase hexadecimal exact length/ushort frames, retaining original host path normalization. This supersedes the prior serializer-only pending claim. | Both fixed controls pass Windows; exact copied DLL floor gate also passes. |
| Diagnostic key parsing | OLD actual AssertEquivalent3 fails: two PE filenames containing a pipe truncate and project-edge mismatch prints opaque context identities. Preserve mapped reference filenames alongside keys and use a fixed safe project-edge mismatch message; never parse identity serialization for display. | Causal3 established within native-bounded51:48pass/3expectedfail. Production correction saved; fresh runtime pending. |
| Final compiler uncertainty and drift | Actual custom Csc completion now downgrades only the dedicated unavailable-execution cause after original immutable inputs/current grants are rechecked. Real capture-child authored mutation still refuses as input drift, without projection or new authority issuance. | All3 pass Windows and Linux floor. |
| Cross-host scope | Exact copied DLL with SDK10.0.400 passes expanded21 classes:136pass/0skip/22s. Includes fixed alias34, physical-key2, final compiler/drift3 and output epoch9. | Focused runtime qualification only; no full build, aggregate, review, merge or row closure claim. |

Production anchors: `NativeCompilerContextCertificate.BindNormalized`, `NativeCompilerTaskInputs.ReferenceIdentity`, `SolutionFileMutationPlanner.ReferenceMetadataKey/MetadataIdentity/AssertEquivalent`, `PhysicalProjectIdentity.Key`. Regression anchors: `NativeNormalizationBindingTests`, `NativeReferenceAliasIdentityTests`, `PhysicalProjectIdentityComparerTests`, `NativeReferenceDiagnosticTests`, `NativeFinalCompilerUnavailableTests`.

2026-10-16 current native diagnostic181 total176pass5fail includes causal remove and membership failures at INITIAL executable fork comparison (WorkspaceForkApplyService line220), before restore. Preflight copied old assets correctly prove App implicit Contract; executable copy intentionally omits selecting assets, whose loaded native graph has Library only. Preserve separate Expected postrestore lineage and derive required InitialExpected from exact omitted-assets proof, removing only certified implicit nonauthored edges; keep aliases/context and strict comparator. Fix in progress; no native green/acceptance claim.

2026-10-16 resume: native context diagnostic proved CurrentSolutionConfigurationContents had different escaped representations with equal once-decoded values; all other globals and tools matched. Normalize only explicit MSBuild escaped-property boundaries, retaining exact semantic hashing and literal-percent distinctions. Further review found final compiler replay used load-wide globals instead of frozen per-owner solution/reference globals; implement exact frozen owner contexts with ambiguity refusal and native conditional-context regression before shipping.

2026-10-16 correction to preceding resume note: static-graph owner globals are NOT Roslyn native-load globals. Fixed15 disproved the speculative SolutionName/AdditionalProperties replay; local Roslyn 5.9 IL confirms its loader forwards only its actual properties and adds SolutionDir during solution Open. Removed speculative owner-context record/tests from product source, retained investigation copies in scratch. Correct fix freezes actual post-open MSBuildWorkspace.Properties for final compiler replay; corrected SolutionDir regression and old-production causal verification pending. Escaped-boundary normalization is separately verified by fixed15, including unrelated missing-assets certification.

2026-10-23 actual current24 prerequisite controls EvaluatedBuildGraphTests.ActualSourceAndFork_PreserveEdgeContextAndCallerGraphMode both fail Edge.conditional (False1m18,True1m29). Independent rawMSBuildWorkspace and managed snapshot both omitEdge; false omitsGraphOnly andtrueincludesGraphOnly, so graphmode behavior itself matchesrequestedcase. Managed evaluatednode carriesEdgeFlavorselected/noInherited; fixtureEdge conditionalso requiresTFMnet10. Native effective child globals/compiler context must be rederived before calling product regression or changingfixture. Do not weaken edge/global-removal assertions. Root initial tail inference that false hadGraphOnly was withdrawn after reading full per-rowlog. Runtime suite stillactive; no fullgate claim.
