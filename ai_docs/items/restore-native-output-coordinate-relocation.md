# restore-native-output-coordinate-relocation — restore-native-output-coordinate-relocation

**row:** `restore-native-output-coordinate-relocation` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BuildPathRelocation.cs`
- `src/RoslynMcp.Roslyn/Services/BuildPathRoles.cs`
- `src/RoslynMcp.Roslyn/Services/NativeConditionEvidence.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphLayout.cs`
- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs`
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs`
- `tests/RoslynMcp.Tests/RestoreBoundaryRegressionTests.cs`
- `tests/RoslynMcp.Tests/RestorePathSlotNativeTests.cs`
- `tests/RoslynMcp.Tests/BuildPathAliasProvenanceTests.cs`
- `tests/RoslynMcp.Tests/NativeConditionProtocolTests.cs`
- `tests/RoslynMcp.Tests/NativeUsingTaskConditionTests.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedExecutionIdentity.cs`
- `tests/RoslynMcp.Tests/NativeUserExtensionIsolationTests.cs`

## Acceptance

- [ ] Pure transport and real native fork controls cover ProjectAssetsFile, ContentPreprocessorOutputDirectory and TargetPath through absolute authored and global paths; fork outputs resolve within owned fork coordinates.
- [ ] Source assets and authored input bytes remain unchanged; exact execution identity and compiler-input comparisons remain enforced.
- [ ] Bind output ownership to actual consumed coordinates and declaration-specific native history; inactive, overwritten, reassigned and semantic alias values remain exact.
- [ ] Replay executed predicates and imports at their native property/item pass and file-relative base; preserve complete escaped Exists lists, directory/missing identities, short-circuit boundaries and native scalar-function results.
- [ ] Verify supported current/floor runtime protocols, Release behavior, fresh-process existence-cache refusal and Linux pre-expansion separator normalization; unsupported ambiguous provenance refuses before effects.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.

2026-10-16 active-provenance expansion: the shared raw name closure also relocates a SemanticValue global referenced only by an inactive output assignment, and rewrites the later semantic value of a reassigned output alias. Both direct BuildPathAliasProvenanceTests controls failed on old name-only behavior. Complete the same helper root using same-evaluation active declaration and input-consumer evidence (ordered evaluated property bindings, actual item/import origins); keep arbitrary alias declarations/global values exact, freeze only path consumers. New anchors: Services/BuildPathRoles.cs, Services/EvaluatedBuildGraph.cs, Services/BuildPathRelocation.cs, Services/PhysicalGraphLayout.cs, Services/SolutionFileMutationPlanner.cs, tests/RoslynMcp.Tests/BuildPathAliasProvenanceTests.cs and RestorePathSlotNativeTests.cs. Preserve multi-context suffix correlation and current path policy for exact output/input coordinate inventories; no recursive copying of coordinate roots. Verification pending.

2026-10-16 consumed-role/condition causal expansion: history-condition-old1 observed 8 passed / 9 failed / 17 total. OverwrittenOutputDeclaration_DoesNotOwnHistoricalScratchCoordinates exposes unused historical alias-prefix ownership; ActiveAliasCondition_UsesPrivateMarkerAndPreservesSemanticAlias source/private (false,true) and (true,false) expose source-dependent Exists operands while (true,true) passes. Require ownership only for final consumed native outputs/inputs, while retaining historical assignment evidence for consumer rendering. Capture physical Condition operands and exact historical import bindings from the same evaluation; preserve semantic alias/global values. Authored native output variants currently fail MSB4066 and require valid namespace-aware fragment rendering; new namespaced causal test pending compiler migration, no claimed proof yet.

Same active-condition mechanism expanded from native evidence: restore-condition-provenance-old1 observed 4 failed / 1 passed (inactive PropertyGroup and ItemGroup descendants incorrectly enrolled denied paths; two native Path.Combine Exists operand source/private differing-marker regressions failed, matching-marker control passed). BuildPathAliasProvenanceTests holds exact regressions. NativeConditionEvidence now drafts same-evaluation profiler + isolated delegated filesystem provenance; native protocol compatibility, precise cache handling, short-circuit/multiple-input controls and fresh runtime verification remain required. No completion claim.

| Condition provenance update | Evidence / required behavior |
|---|---|
| Native context correction | A custom native filesystem requires a fresh Shared EvaluationContext per evaluation; the earlier isolated-context draft was invalid. Parent protocol5: 32 passed / 2 failed; only file-list controls remained red. |
| Remaining causal roots | Parent remaining-old1: 8 failed / 4 passed. Parent forward-imported-old2: 7 failed / 1 passed, including declaration-time forward alias and six imported-relative predicate rows. Preserve native property-pass When predicates, element-specific evaluation directories, complete escaped file-list operands and inactive literal declarations. |
| Fresh cache control | Parent fresh child with MsBuildCacheFileExistence=1 observed the exact ExistenceCache refusal; LoadedRuntime control passed 1 / 1. |
| Current source | NativeConditionEvidence, BuildPathRoles and declaration-local relocation are saved for fresh compiler/runtime verification. LiteralBackslashOperand adds native pre-expansion separator controls; Linux old/new, Release and supported-floor validation remain pending. No completion claim. |

| Native task / query-shape update | Observed evidence / current status |
|---|---|
| UsingTask provenance root | Actual SDK TaskRegistry evaluates task predicates without native condition-profiler frames; strict association previously refused FrameRoot. Public private-cache ProjectAdded instrumentation now wraps the original predicate once with private no-IO sentinels; normal profiler association remains exact. |
| Readonly cache safety | Four actual native cache controls passed: captured original bytes and pure public DeepClone reader reload, each with ordinary and SDK projects; independent cache condition inventories and source bytes remain exact. |
| Task replay proof | Combined native gate observed all ten NativeUsingTaskConditionTests passing, including imported task registration, source/private marker divergence, duplicate declarations, exactly-once marker probes and zero skipped-operand probes. |
| Directory query causal proof | Six DirectorySuffixPredicate cases failed old capture after independent native truth premises passed. Controlled SDK diagnostics identified equal physical paths with differing retained directory-suffix spellings; raw native query suffix is semantically significant for Exists(file/). |
| Query-shape correction | Saved capture separates canonical physical inventory identities from raw native query spellings, compares directory-suffix intent and retains exact cache/query semantics. Two native function-returned suffix controls added. Fresh full verification pending; do not claim initiative complete. |

Native invocation/cache completeness evidence: parent cached5 causal run failed all five after native truth premises; the blanket post-evaluation import-cache shortcut skipped actual queries and invented execution for short-circuited ordinary predicates. Corrected core gate compiled and passed native SDK minimal, cached5, UsingTask10 and unquoted3. Follow-up IdenticalPathPredicates_RetainExactExecutedOccurrence failed skipped-first=True (private expected empty, actual true), proving source-order correlation cannot identify repeated operands. Current source generalizes exact private per-Exists invocation boundaries to every evaluation-time condition, retains whole UsingTask boundaries, and grants PRE-cache semantics only to Import/ImportGroup. Current-source verification pending. CachedImportFunction2 and suffix8 passed after native-observation premise corrections; no opaque-function unsupported-policy claim is warranted without an actual zero-query witness.

Quoted native function completeness: restore-lifetime-quoted-old1 observed four failures for backtick-quoted filenames containing only opening or closing parentheses; both balanced-filename controls passed after independent native truth premises. IsScalarStringFunction counted filename parentheses as function syntax and refused valid observed scalar paths. Current source reuses BuildPathRelocation.ParenthesizedEnd, the shared quote-aware lexical span scanner, for scalar expression extent validation; no function executes again. Six native source/private selection controls retain unrelated-I/O exclusion, exact marker capture and unchanged source bytes. Fresh corrected-source verification pending.

Native extension-directory isolation: restore-native-dependency-extension1 observed private extension retained control pass and private extension removed failure (expected false, actual true), after real SDK source import and production private copy/load premises. MSBuildUserExtensionsPath remained source-bound; SDK-origin condition/coordinate filtering hid the owned native input root. Current source gives MSBuildExtensionsPath/32/64/UserExtensionsPath one shared native input-directory role, captures roots only from actual consumed imports/predicates/items using declaration-time bindings, enrolls mutable roots for mapping and exact condition identity (not recursive copies/output grants), and preserves independently verified SDK roots externally. Execution identity includes native input coordinates. Eight additional consumed/unconsumed slot controls and corrected-source native replay await parent runtime.

Bounded native extension green receipt: restore-native-extension-fatal-fixed1 passed all thirteen in 17 seconds (ten extension role/isolation controls plus three mandatory source-assets/CLI denial controls). Parent verified unchanged compiled DLL SHA 770CC7EEEC420CC7071B5311700AD2943140EA4B87FB4BBC4ADDD89A7AC8F25B before/after; compiled role-source SHA BA462FFC5951AFE6D6BB48C03F4C22F574F7F01C9D600B7962889394D43519D3. Private removed-extension causal red is now corrected; consumed/unconsumed four native input-directory slots preserve global identity and do not grant writable output roots. This is focused snapshot proof, not final-head/full aggregate/Release/floor qualification.

Observed query epoch causal: restore-observed-condition-old1 failed both deterministic policy-hook create/delete cases (no refusal) after native branch premises; regular-file suffix controls both passed. Required ConditionProbe now retains raw QueryPath and nullable native ObservedExists; real queries/cache hits carry paired truth, synthetic coordinates/unqueried complete-list members carry explicit unknown. New shared ConditionInputTruth validates canonical identity and logical Exists from captured PhysicalInputKind, preserving file-with-directory-suffix false. Layout and source-coordinate stage discovery bind observed truth to the exact current authorized identity; overlays retain separate intended kind and final private candidate certification. SDK exact-existence receipts and generic compiler Epoch consume the same pure helper without re-evaluating functions or hashing metadata-only candidates. Current source is frozen for joint runtime, no green claim yet.

Parent causal restore-registry-literal-context-old1 ran BuildPathRolesLiteralItemTests10: 8 failed / 2 passed. Absolute Include/Update/Remove/Exclude4 and escaped %2A/%3F direct/property4 established native literal item/metadata premises then observed invented parent ConditionProbe; real wildcard direct/property2 passed native two-file enumeration and required-directory-role controls. Same root made genuine verified TestHost Content and MSTest Analyzer EditorConfigFiles package literals require unrelated parent-directory grants, degrading native compiler authority and disabling shadow isolation. Source correction preserves all InputCoordinate relocation anchors, detects wildcard syntax in the escaped expanded token before Unescape, and adds a synthetic directory probe only for actual unescaped glob tokens. Literal files remain exact physical/package proof inputs. Lifecycle test cleanup now retains primary assertion plus all cleanup failures in the exact two observed methods. Fresh fixed tests/native lifecycle runtime pending; row remains open.

Causal precision correction supersedes the preceding old10 summary: literal Include/Update/Remove/Exclude4 failed the invented parent ConditionProbe assertion. Escaped %2A/%3F4 passed exact native literal-item premises but failed EARLIER because PathEvidence.InputFiles omitted the literal path. Both real wildcard controls passed. Root completion now separates already-native-evaluated item/HintPath exact paths from raw pattern/property slots, preserves literal metacharacters and MSBuild-looking filename text, detects glob/static-prefix syntax before unescaping, and removes the equivalent unresolved-token filter from evaluated Analyzer/HintPath artifact inventory. New filename/directory/direct/property expression controls and exact artifact cases are source-only until parent runtime. No acceptance closure or fixed-runtime claim.

Parent fresh Windows literal36 gate passed all36 (combined59 duration1.8s), covering exact evaluated item/artifact filename and directory text, direct/property escaping and genuine native globs. Further same-helper native Exists audit produced tests-first12: all12 failed with Operand after independent native Exists truth/path premises (restore-literal-exists-old1). Exact query arguments are not globs; raw unresolved-expression checks remain before decoding, followed by the shared exact physical canonicalizer. Source query fix saved; combined literal48 current/floor and full native/aggregate verification pending. Row remains open.

Complete native-value caller audit supersedes the tentative raw-property guard wording above: all remaining TryPhysical/TryPhysicalAt callers consume native GetPropertyValue or evaluated Binding.Value, not raw expressions. Tests-first restore-literal-property-old1 ran69: item36/query12 all passed, property21 all failed lost-path assertions AFTER independent native scalar-value premises (OutputPath/TargetPath/keyfile/consumed native input directory across five escaped groups plus raw scalar wildcard). Removed the obsolete guarded helper and routed every evaluated property/item/metadata/query caller through the shared exact-path canonicalizer. Raw unresolved-condition checks still precede decoding; item glob/static-prefix checks still inspect escaped raw text. No URI behavior change or optional shim. Fresh complete69 runtime pending; row open.

Fresh Windows current SDK parent gate restore-literal-registry-context-fixed3: all69 literal/native-query/native-property controls passed. Combined gate91passed/1failed/92total in2.7163s: registry2 and graph19 all passed, direct-context control passed; nested-context receipt attribution remains an independently causal failure owned by the native writer receipt correction. This verifies literal exact-path canonicalization and retained true-glob behavior only. No full aggregate, merge, complete native lifecycle or Linux floor claim yet; row remains open.

Parent same-DLL Linux qualification literal-registry-context-fixed3-linux:91passed/1failed/92total/0skipped. Literal69+registry2+graph19 all passed; direct-context control passed, only nested-context old attribution failure remains. Script run-literal-registry-context-linux.sh verified copied DLL comparison and SHA. Log/TRX /home/<user>/.codex/scratch/roslyn-correctness15/literal-registry-context-fixed3-linux.log and .trx. Host SDK10.0.400 is known; exact Locator-selected loaded MSBuild ABI still awaits explicit runtime identity output and is not inferred from the host version. Focused runtime portability only; full aggregate/native lifecycle/merge and row closure remain pending.

2026-10-16 resumed prerequisite review: exact immutable native project items have two roles: external absolute import definitions and private native content candidates. Existing MapInputPath conflated those roles. Current correction separates definition mapping and requires both-side exact comparison mapping, with current original/private hashes and package manifest/member proof plus current grants. Preserve mutable placement, aliases, interop and generated-config identity. New absolute-only and mixed-role native controls and adversarial copy/grant controls are being verified; no acceptance or full-gate completion claim.

2026-10-16 resumed causal evidence: native graph comparer used unique actual dictionary versus expected path set; three duplicate-document controls failed on old implementation. Strict multiplicity and ordered text comparison now passes nine comparer controls. Fork post-restore still exposes document refresh multiplicity mismatch and remains red. Separate exact-file provenance control fails because mixed immutable roots classify a descendant of a package file as immutable; repeated mixed-root scans are quadratic in package members. Repair typed exact membership plus directory roots while retaining current-grant/hash checks; no acceptance or full-gate completion claimed.

2026-10-16 follow-up: native trace proves mixed NuGet/default-glob source documents2 shared path became external/private paths after replay; refresh retained both and appended a third. Refresh now selects only manifest-eligible package-origin occurrences with exact native counts; unsupported NuGet action regression passes fixed and fails widened membership mutation (predicted2 versus1). Existing fork success fixture source itself produced CS0101 on duplicate Input declarations; corrected unused package content to comment-only, retaining asserted two occurrences and adding zero-error source precondition. Native real-test timeout remains red; typed-index tests pass, no threshold changed, focused timing investigation ongoing.

Current-session authority-integrated1: 74 pass, 2 fail, 5 platform skips. Replay real test still hits unchanged 120-second request timeout; Windows ordinary-component attribute fastpath was compiled, so no passing performance claim. Fresh sampled perf-current trace attributes 9.31 seconds of 20 to physical resolution, 8.08 to component attribute queries; no permission or identity caching permitted. Cold review also identified retained exact package provenance after WithAdditionalInputs/Union replace selecting package authority, allowing replay fallback to recapture old members without retained package proof; complete proof/provenance repair and regression pending. Degraded semantic document load exposed separate mandatory-document and manager readiness conflation; targeted fixes remain under validation. No acceptance closure or shipping approval.

Retained-proof causal e1e66cc7: two new controls fail on old retention/fallback behavior, signed controls2 pass. Fixed retained-degraded3: 81 pass, one stale restoreRequired test expectation, five platform skips. Ordinary Compile denial remains mandatory; broad new fallback was corrected to typed aliased-path-only, existing NativeMandatorySourceAuthorityTests6 preserved. Current host policy uses opaque issued-refusal classification preserving exact public exception types; mutation alias checks remain strict. Whole-path fresh Windows handle probe allowed actual real test to pass unchanged120-second preparation and reach validation; realtest-handle3 failed later, not timeout. Guarded frozen-input diagnostic24ff09e4 reveals fork obj/project.assets.json changed File-to-File after validation: classic dotnet test implicit restore currently replaces frozen dependency outputs. No assertion removed; all diagnostic source restored. Common test preparation root repair pending, no ship or acceptance closure.

2026-10-16 resume verification: mandatory-retained-fixed4 passed 86 tests with 5 platform skips. Broader native-integrated5 remains running/red: invalid-assets repair flag; unowned SDK user-extension fixture; missing PackageDownload exact installed-file proof; configured cache root incorrectly rejected as restore destination; concurrent undo 30-second limit. Fixes retain assertions and authority checks. Classic and native test preparation now share frozen dependency receipts and --no-restore with current certificate reuse; pending focused/native validation. Restore/extraction remain unshipped; paused batch unchanged.

2026-10-16 current verification: guarded download/destination causal run cdead50d produced six expected failures; fixed8 passed137/failed11. Download proof, alias reconciliation and queued-start authority controls pass; explicit test-runner contract exposed test doubles injecting failures during restore, now migrated without dropping assertions. New unrelated-assets fixture native certification and real fork explicit test execution remain red/investigated. Full gate and shipping have not begun; extraction and paused batch unchanged.

## 2026-10-17 native coordinate causal qualification

| Mechanism | Current-session evidence |
|---|---|
| Generated editorconfig recognition | Linux124 observed native SDK fallback obj\Debug/net10.0 coordinate retained a literal backslash through Path.GetFullPath; document actually uses obj/Debug. Strict comparison correctly refused unrecognized generated content. |
| Native item coordinates | Linux125/127/130 independently evaluated FullPath differs from direct evaluated Include conversion for project references and compiler declarations, including escaped percent/semicolon names. |
| Consumer-specific fix | EvaluatedProjectGraph uses original escaped generator property through native TaskItem.ItemSpec; native item readers use FullPath metadata. Scalar assets/content-preprocessor and installed RAR HintPath consumers retain their distinct semantics. |
| Escaping oracle | Linux130 actual SDK generator emits a literal backslash for escaped %5C; native item FullPath instead normalizes it. Do not substitute item metadata for task FileName semantics. |
| Focused fixed-reader result | Linux133 diagnostic run: 22 passed, 12 graph-provenance failures. Generated configuration and compiler-item controls pass, including actual native task output; this is not full prerequisite acceptance. |
| Exposed condition provenance | All 12 failures bind to Microsoft.Common.props:30; reconstructed MSBuildUserExtensionsPath retains Microsoft\MSBuild while actual query uses Microsoft/MSBuild. Installed native code applies another FixFilePath before filesystem access, after the earlier raw loaded-project-cache lookup. Exact identities must remain separate; no SameQuery relaxation. |
| Remaining | Condition root fix/causal controls; output TargetPath and restore ExtensionsPath native oracles currently blocked by condition provenance; final caller Windows/Linux, full local and hosted verification. |
| Evidence root | %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume; namespace125-linux, namespace127-linux, namespace130-linux, namespace133-linux. |

Keep this row open until complete qualification. No permission, grant, byte, generator-provenance or comparison assertion is relaxed.

## Native cache and expansion provenance qualification 2026-10-17

| Mechanism | Current-session evidence |
|---|---|
| Cache ownership | NativeConditionEvidence owns a nonshared collection; exact private dictionary/comparer identity and retained live roots are verified. Query multiplicity is preserved. |
| Proven JIT loss | Linux142 failure stack omitted inlined Dictionary.FindValue; retained-root invariant replaced that dependency. Linux143 all129 controls passed. |
| Remaining cache loss | Windows146 and Release153 still lose the expanded argument of a true cached import. Release153 failure-only evidence has correct invocation/result, zero filesystem inputs and zero cache arguments; no protocol failure. Not fixed by subsequent diagnostic passes. |
| Expansion-origin defect | Causal155 actual native property-function callback returned empty after probing the owned filesystem. All premises passed; old ordinary fallback incorrectly admitted the expansion probe as the original path. |
| Complete origin fix | Required ArgumentExpansion field retained at both ExistsProbe construction sites. BuildPathRoles rejects expansion inputs before every fallback, cache, literal/list and task replay branch; no filtering, assertion relaxation or default constructor. |
| New regression | Isolated child process enables test callback functions without mutating parent environment. Ordinary-empty, import-empty and true cached-result cases all require exact OperandOrigin refusal. Cleanup preserves primary failure. |
| Fixed controls | Release156:80PASS1FAIL81; new origin cases pass. New cross-platform case fixture incorrectly mixed separators, causing real Windows filesystem lookup; fixture now varies case only, exact assertions unchanged. Release157 both origin and case controls pass. |
| Open verification | Late cache argument loss, original undo deadlines, pending operator decisions on inherited native assertions, Windows/Linux complete qualification, full local and hosted gates. |
| Evidence | %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume namespace153-windows-release, namespace154-windows-release, namespace155-origin-causal, namespace156-origin, namespace157-origin-cleanup. |

Keep the row open. No complete restore acceptance, shipping, extraction integration or batch advancement is claimed.

## Cache helper inlining root cause and repair

| Evidence | Result |
|---|---|
| Native JIT160 | Optimized Get embeds the first null-loader GetOrLoad lookup; optimized BoolEvaluate can call GetOrLoad directly. Warming both together hid the unsupported helper-frame assumption. |
| Causal161 | Fresh child warmed only exact owned TryGet against a retained condition-free root, then first VerifyProtocol failed CanaryOrigin. |
| Diagnostic162 | Actual failed stack is comparer.GetHashCode -> PRECache.Get -> TryGet -> native BoolEvaluate, with no GetOrLoad. Native cached result true, zero filesystem inputs, zero recorded arguments. |
| Narrow repair | Removed only redundant GetOrLoad-frame predicate and unused comparer type argument. Retained positive native Exists, expansion exclusion, exact active scope/depth, owner/dictionary/comparer checks, retained-target validation, mutation exclusions and recursion cleanup. |
| Assertion integrity | Poisoned-entry tests now enumerate the exact stored key for post-refusal inspection instead of re-entering the comparer; same object-identity and disposal-refusal assertions remain. |
| Integrated164 | Normal Release:354 passed,3 failed,3 platform skips,360 total. All cache/origin controls passed. Remaining failures are two original30-second undo cases and the real120-second fork/test request. |
| Remaining diagnosis | Real deadline expired during PhysicalGraphCopyPlan.CopyAsync; obtain current per-operation native/copy/package cost evidence before selecting performance work. No deadline changes or cache-argument reexecution. |

Full local/hosted validation and shipping remain outstanding. Keep the row open.

## Native item-coordinate completion 2026-10-17

- Two further native item captures used host parsing of EvaluatedInclude: NativeBuildMembership.FileItems and CompilerProjectReferences declarations. Independent installed-MSBuild oracle Windows1768PASS; Linux1772FAIL6PASS on escaped %5C.
- Structural search found the same mechanism in EvaluatedBuildGraph analyzer artifacts and Compile/AdditionalFiles/EditorConfigFiles inventories. New independent native graph tests Windows18216PASS; Linux1834FAIL12PASS, one escaped %5C failure per item kind.
- All four production capture pipelines now use the native item FullPath. Reference.HintPath and build-task scalar interpretation remain unchanged. Literal-item test helpers now observe native item FullPath while retaining exact expectations.
- Windows184 and Linux185 expanded106 tests allPASS/noSKIP. Every native item consumer was inventoried in scratch/native-graph-item-coordinate-review/README.md; no additional instance found.
- Full aggregate and deadline qualification remain outstanding; this coordinate fix does not claim a latency improvement.
