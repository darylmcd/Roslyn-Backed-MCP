# Bounded native output preparation and final admission

## Current source defect

| Anchor | Observation |
|---|---|
| WorkspaceSessionLoader.cs66,81,96 | Epoch captured before native Open; Open may legitimately create an evaluated obj directory; Complete demands old Missing identity. |
| NativeCompilerContextCertificate.cs342,406 | Physical input population includes output/condition coordinates, then strict AssertCurrent rejects native-produced directory transition. |
| NativeCompilerContextCertificate.cs147 | Zero-edge projects skip extra Csc capture used to restore omitted edge metadata. Final native Open still supplies actual options/metadata; this alone is not a defect or reason to force missing-assets producer success. |
| NativeCompilerTaskInputs.cs52-76 | Trusted design-time compiler producer can itself create outputs after the Open; one provisional Open alone does not guarantee later normalized capture is stable. |

## Required phases (one preparation, one final load; no retry loop)

| Phase | Required ownership/proof |
|---|---|
| 1. Preparation declaration | Evaluate original graph with actual globals/inner framework contexts. Freeze authored source/project/import/config/response/analyzer input identities, relevant wildcard memberships and current dependency proofs. Independently identify exact evaluated output paths/required directory coordinates. Validate current Host policy before explicit input/output probes. Output declarations never grant authored, SDK or package authority. |
| 2. Provisional producer | Open a private MSBuildWorkspace solely to execute legitimate design-time preparation. Do not publish Solution, certificate, images, parser receipts or analyzer lease. Capture trusted Csc task in every inner owner context that final edge normalization needs, with actual command-line/options/input validation. Do not force extra no-edge producers without demonstrated capability need. Existing producer owns child/channel cancellation and drains before release. |
| 3. Transition validation | Assert all frozen authored/external input identities and memberships unchanged. Accept only declared output transitions, with physical canonical identity/overlap checks: a missing exact output directory may become a directory; exact declared generated output files may acquire/change bytes only under their declared native producer role. Never classify an entire obj subtree as output-owned or ignore arbitrary directory differences. A path that also has authored/input role cannot be waived as output. Reject file↔directory swaps, symlink target changes, undeclared condition/input changes and external input mutation. |
| 4. Dispose provisional state | Dispose provisional workspace before discarding its diagnostics/state; no analyzer shadow lease or published metadata authority survives it. Preserve preparation errors/cancellation and aggregate cleanup failures. Preparation diagnostics are not final semantic diagnostics. |
| 5. Fresh capture | Re-evaluate from same authored/global identities after stabilized outputs; current-policy reauthorize the full actual graph and recapture package/SDK/current grant identities and predicate truth. New generated imports/documents must be explicitly declared/proven outputs; newly observed authored/external inputs require ordinary authorization and exact frozen-input consistency. |
| 6. Final native load | Create a FRESH workspace/diagnostics sink; Open once. Complete exact epoch and capture every required edge-normalization task receipt; compare actual native options/references for every inner context, including no-edge projects. Assert final native graph/arguments/references/options match recaptured evaluation and exact inputs unchanged after Open AND producer capture. Any further transition is a deterministic refusal; do not recapture/retry it into acceptance. |
| 7. Final publication | Capture admitted immutable image buffers only from final original reference objects; prepare analyzer isolation using final identities; revalidate policy/epoch before atomic manager swap. Transfer only final workspace+lease+certificate. On failure retain old session; dispose all new owners. |

## Output classification requirements

- Use evaluated producer outputs/roles, not string containment under obj/bin or a blanket directory permission.
- Missing→Directory acceptance applies only to exact declared output directories and necessary declared output ancestry, with canonical physical target proof. It does not accept arbitrary files inside the created directory as inputs.
- A newly created file consulted by Exists/import/source/reference changes must have an exact generated-output origin or ordinary authored/external input authority; an output directory role does not prove that file's bytes.
- Native producer receipts must identify actual compiler output and generated task inputs in each context. Csc output receipt alone does not prove arbitrary custom-task writes; unsupported output ownership remains explicit refusal.
- Preserve missing/invalid assets degraded semantic-load behavior. Do not run restore/build or prepopulate fixtures to hide fresh-load requirements. Native output preparation is not package restoration or a dependency grant.
- The normal final epoch remains strict: only the explicitly bounded preparation transition is special; ordinary reload/preview certificate checks never waive changed inputs.

## Diagnostics, cancellation and recovery

| Concern | Required behavior |
|---|---|
| Diagnostics | Provisional and final sinks distinct. Publish final diagnostics only; report failed preparation through existing safe redactor, no raw property/native output leak. |
| Cancellation | Check before each phase and before final publication; await/drain owned child+pipe, dispose provisional/final resources exactly once. Cancelled preparation produces no session/certificate. |
| Disk effects | Native design-time tasks already execute trusted code and may emit declared outputs. Do not promise full disk rollback from snapshots alone. The preparation protocol authorizes no new arbitrary writes; unknown native writer effects need an explicit policy/owned execution contract. |
| Mutation/undo | No mutation lease, preview token, undo version or current session swap during preparation. Output ownership receipts must not be replayed as authored overlay input authority. |
| Failure | One deterministic attempt per phase; strict final mismatch fails closed. Preserve old loaded workspace on failed reload. |

## Acceptance corpus

| Shape | Expected |
|---|---|
| Clean graph with B/obj absent, project edge | One bounded preparation, final load/capture succeeds and reference matches actual Csc. |
| Same graph with no ProjectReferences | Final independent native options/reference identity required; additional manual task capture only if its contract is actually needed. |
| Missing package assets | Preserve exact no-PackageReference missing-state physical authority; genuinely invalid/unverified package dependencies retain their separate unavailable executable context. No hidden restore. |
| Existing stable obj | No regression, final identity strict. |
| Native preparation edits authored source/import/global config | Reject; no publication. |
| New output directory contains undeclared condition/import/input change | Reject; no subtree waiver. |
| Shared/authored output path collision, symlinked output target | Reject or require exact existing policy; output role cannot launder input authority. |
| Final native phase changes supposedly stabilized output/input | Refuse without third attempt. |
| Multi-target owner contexts and metadata-only project | Compare final actual native options/references for all contexts and manual Csc receipts for contexts where edge normalization requires them. |
| Cancellation/failure during each phase | Both private workspaces/child+pipe/leases disposed; old session retained. |

Status: implementation and verification remain incomplete. Use the checkpoint tables below for exact causal, positive-control and active-gate evidence.
## Missing restore artifact constraint

- Preserve PhysicalGraphStageTests.CopiedRestoreInputAbsence both rows: missing assets/dgspec remain absent through staging and are bound until publication. Never pre-restore the fixture or run restore during preparation.
- NativeCompilerTaskInputs.ReadAsync demands entire producer Succeeded; source does not prove missing-assets task success. Parent should probe if a needed edge-normalization context has absent assets; do not infer failure/success without actual evidence.
- Prefer one provisional native Open plus exactly required task producers, then fresh epoch/final native Open. Do not invent separate physical replay authority unless tests show the current independent native snapshot cannot establish exact broken-state replay without overclaiming executable authority.
- If a split becomes necessary: replay authority binds exact authorized authored/config/input bytes and missing outputs plus graph/native snapshot; executable image/compiled analysis authority additionally requires actual admitted implementation images and verifier proof. Never silently grant one from the other.

## Initial implementation checkpoint

| Source | Implemented contract |
|---|---|
| WorkspaceSessionLoader | Owned preparation receipts for every original inner context, then provisional private Open; dispose; fresh capture/final Open. Final Csc proof remains limited to required edge normalization; no retry or provisional publication. |
| NativeOutputPreparation / Epoch | Exact output-role transitions only, protected authored/config/package/restore identities, current mutable authorization before reads, independently proven SDK/package inputs retained; declared directory reparse transitions refuse. Fresh input inventory cannot introduce undeclared paths. |
| BuildPathRoles / EvaluatedExecutionIdentity | Required exact OutputFiles inventory captured/hashed; final execution identity binds output-file coordinates. Duplicate-node graph comparison also binds exact OutputFiles; added after parent diagnostic restoration. |
| NativeCompilerTaskInputs | Shared raw owned child/channel lifecycle; provisional receipt discarded, final parser/reference validation remains required for edge normalization. No unconditional no-edge task invocation. |

## First implementation feedback

| Check | Observed result / correction |
|---|---|
| Initial focused49 | Parent40passed/9failed overall. FreshNoEdge, absent assets/dgspec2, authored mutation and changing declared output controls pass. Restored edge fails at preparation classification. Other source-owned failures are tracked by their authors. |
| Exact edge diagnostic | prepared changes Missing→Directory; mutable, no protected role, exact directoryRole=false. Final IntermediateOutputPath is a nested declared leaf, requiring its parent chain. |
| Necessary ancestry fix | Shared role helper accepts only exact leaf or necessary ancestor within project/consumed output boundaries, with protected inputs and current policy unchanged. Required-parent/sibling/file controls added; fresh runtime pending. |
| Parent-link hypothesis correction | Both initial tests failed fixture premises. Existing PhysicalInputIdentity.Capture calls EnsurePhysicalPath, which rejects any filesystem-link component before hashing. No product gap established; tests now assert existing early-rejection contract. No resolver or policy weakening. |

## Necessary-ancestor checkpoint

| Check | Observed status |
|---|---|
| Own initial6 | Fresh no-edge, missing assets/dgspec2 and actual mutation/changing-output controls pass; restored edge fails exact preparation directory classification. |
| Ancestry/path5 | Parent reports all5 passing: necessary parent accepted, sibling/file rejected, existing/new link components rejected by the established pre-hash contract. |
| Restored edge after ancestry | Advances past prepared Missing-to-Directory, then refuses an introduced physical input during fresh recapture. Exact generated/input role diagnostic prepared; no broad waiver or expected-success weakening. |

## Native producer and source-reference checkpoint

| Check | Evidence / remaining obligation |
|---|---|
| Introduced-input diagnostic | Dependency.AssemblyInfoInputs.cache, Dependency.assets.cache and Dependency.GeneratedMSBuildEditorConfig.editorconfig are new mutable final inputs, without original exact output roles. Derive actual trusted producer receipts; do not waive cache names, final None membership or obj subtrees. |
| Native source-edge diagnostic | Actual Y.dll Csc input independently matches exactly one loaded source target and parsed operand; physical candidates are Missing. Source CompilationReference must retain authorized absence without requiring a built metadata PE. |
| Source-role implementation | Required explicit native reference population, exact absolute operand/path/aliases/interop/kind match, frozen output identity and distinct source role. Native reference7, strict metadata search4 and both actual reactivation cases pass. |
| Alias identity causal | One localized historical host-comparer substitution fails Alias versus alias equality. Exact producer source original/restored SHA25629B6F62A8AE1017E1B9DAF40207446997780F1EE6D04F08930B399C352BE3DB0. Fixed fresh gate active. |
| Logger discovery correction | Initial build compiled but test-local ILogger caused pre-initialization MSBuild.Framework discovery load. Actual observer moved into production and instantiated after initialization; no framework copy or suppression. |
| Public relay capability | Actual writer executes but relay exposes Source3/4=False and zero target/task/parameter events. Proven unavailable receipt route; no weakened boundary or framework DLL copy. |
| Owned producer replacement | Existing owned child/channel streams successful writer facts for every original inner context BEFORE provisional Open. Original-context target definition, exact SDK implementation hashes, typed output parameter, independently declared scope, current policy and protected input roles bind output byte identities; final Csc proof still requires whole-producer success. |
| Original property binding | MicrosoftNetBuildTasksAssembly is a required CompilerProjectReferences field captured from the original evaluated instance and bound by context consistency. No project re-evaluation selects task authority during preparation. |
| Focused1321 | Preparation6, path5, source-reference7, metadata search4 and owned writer1 pass. Combined45 is39passed/6failed; graph fixture and analyzer lifecycle/image cleanup remain red, so no initiative completion claim. |
| Next proof boundary | Authority9 controls and actual lowercase MSBuild task spelling installed tests-only. Production spelling classifier unchanged until parent causal run; wrong expected SDK hash changes no physical SDK file. |
| Fresh combined runtime | Parent55595 active at this checkpoint. Other authors own image/lifecycle/source-prefix failures. No aggregate green claim. |

## Registry and producer-identity checkpoint

| Check | Current-session evidence / next requirement |
|---|---|
| Original registry ABI | Initial active capture failed native descriptor assembly ownership, then dictionary-only task-host reading. Actual native metadata places AssemblyLoadInfo in Framework and stores all five TaskHostParameters properties; checked source corrected without factory invocation or property-function replay. |
| Current and Linux floor qualification | Same compiled DLL:91passed/1failed of92 on each host. Registry2, graph19 and literal69 pass; actual nested same-project/different-globals task executes but wrongly borrows original output authority. |
| Factory identity causal | Actual RoslynCodeTaskFactory body executes from trusted Tasks.Core factory DLL; old admission grants one output. Factory assembly location alone does not establish builtin task implementation. |
| Spelling causal | Actual lowercase writelinestofile executes its output but old collector emits no receipt. Canonicalize only the supported short and fully qualified native names. |
| Verified ordinary producer correction | Actual execution uses NodeId/ProjectContextId/SubmissionId; evaluation metadata is separate. Ordinary writer context2, Csc context2, observation2, factory1, registry2 and receipt authority9 pass after checked short/full implementation matching. |
| Cached-selection causal | Override records bypass native exact/fuzzy caches. After complete existing-record coverage, x64 builtin control passes but actual x86 custom body wrongly borrows one historical builtin identity; restore-selected-cache-causal-cleanup-fixed1 records the admission failure. |
| Mixed created-wrapper correction | Preserve every matching custom/unverified created wrapper as uncertainty; verified candidates must share complete implementation and five-field host identity. Uninstantiated registrations grant no authority; selection2 subsequently pass. |
| Original-registry unavailable boundary | Dedicated cause survives graph wrapping; LOAD-only epoch snapshots current-authorized entry/configuration identities and issues no compiler/image/physical authority. Semantic-load, entry-drift and current-denial controls all pass. |
| Child execution unavailable causal | Actual mixed-history output enrolled through AdditionalFiles causes undeclared-input semantic-load refusal; irrelevant output remains Certified and authored-file mutation refuses. Dependency-sensitive reconciliation retains original baseline plus strict final snapshot/current grants and issues semantic-only TaskExecutionUnavailable; all3 fresh controls pass. |
| Stronger Csc fixture corrections | Initial omitted obj setup and subsequent mixed scalar/item-list marker both failed execution premises; bounded diagnostic identified MSB4012. Materializing the marker in a scalar property establishes actual nonempty CommandLineArgs, with an independent complete native run for intentionally cancelled nested captures. |
| Fully qualified Csc causal | Corrected four-case corpus: short direct/nested pass; full direct loses all task frames and full nested misses the actual owner boundary after native Csc execution. Shared checked classifier correction passes all4 fresh cases. |
| Exact context string encoding | Escaped-literal encoding8 observes5 collisions: native context3 and analyzer catalog2; framing/keycase controls pass. Shared length/ushort encoding correction plus Csc4 and full ruleset7 passes all19 fresh cases. |
| Typed compiler uncertainty | Actual unverified Csc implementation executes through AFTER marker; old final capture throws generic refusal and preparation lacks explicit uncertainty. Dedicated protocol fact, typed final refusal and semantic-only preparation handling pass both cases within the fresh38 producer/encoding controls. |
| Exact private IPC causal | Actual frame5 roundtrip loses unpaired high/low UTF16 values on old JSON transport; replacement/pair/ordinary3 controls pass. Shared exact codec, logical quotas and finite5x wire limits pass fresh50; necessary code-unit/cardinality preflight3 plus existing quota9 subsequently pass all12. |
| Final Csc unavailable boundary | Valid named-mutex native fixture retains builtin loading, then selects custom Csc only for final normalization without changing authored files/globals. Old code completes actual custom child and AFTER marker then aborts with dedicated uncertainty; fixed3 pass, retaining semantic projection and refusing installed-package/image authority; actual capture-child authored mutation still refuses input drift before projection publication. |
| Typed edge identity | Old alias34 observes8 valid failures: BindNormalized split/join2, compiler key split1 and planner identity families5; all26 controls pass. Typed project/alias comparisons, immutable compiler alias identity and shared exact planner suffix now pass all34. |
| Host comparer compatibility | Actual Windows physical key source/output2 collide for normalized @ versus Z paths under consumer case-insensitive comparer. Producer now returns uppercase hexadecimal exact length/ushort frames; fixed2 pass on Windows and the exact copied DLL on Linux SDK10.0.400. |
| Private mismatch diagnostics | OLD actual diagnostic3 fails: pipe-containing PE filenames truncate and project-edge output exposes opaque keys. Correction preserves mapped reference filenames beside identity keys and uses a fixed safe edge message; fresh verification pending. |
| Effective toolset binding | Original ProjectInstance.ToolsVersion is captured in the registry witness and required through per-context certificate/producer APIs; child request, event, configuration and effective instance must agree. Default/legacy-project normalize to Current; explicit nested15.0 independently fails MSB4132 and is a native unsupported-request control, not an owner mismatch. |
| Supplemental package transfer | Exact original session/Solution/version, selecting assets, installed package hashes and original-scope current grants bind semantic-only missing-assets reads; no synthetic private assets or compiler/image/physical authority. Fresh package7, toolset3, context encoding7, Csc4, writer2 and selection2 pass25; later corrected native final-Csc/drift3 also pass. |
| Missing authored package declaration | Valid restored source retains actual selecting assets/imported compile document after PackageReference removal before loading. Old private missing-assets load refuses original cache grant; declared-package control passes. Missing-assets condition now also recognizes nonempty supplemental exact-file proof; both declared-reference and no-authored-reference controls pass. |
| Exact package document membership | Directory tool roots and exact verified files are separate; authored-root precedence remains. Four pure exact/descendant/authored/unverified controls pass; no physical descendant escalation was demonstrated. |
| Remaining qualification | Effective ToolsVersion and final-Csc/drift controls pass; exact copied DLL Linux SDK10.0.400 expanded21 classes pass136/0skip/22s. Full aggregate, final integration/review and merge remain open; component gates do not establish initiative completion. |
