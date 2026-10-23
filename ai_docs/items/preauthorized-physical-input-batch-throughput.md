# preauthorized-physical-input-batch-throughput — Bound parallel fresh input proofs after full authorization

**row:** `preauthorized-physical-input-batch-throughput` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PhysicalInputCaptureBatch.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`
- `tests/RoslynMcp.Tests/PhysicalInputCaptureBatchTests.cs`
- `tests/RoslynMcp.Tests/PhysicalInputCaptureBatchIntegrationTests.cs`

## Acceptance

- [ ] Execute immutable, fully preauthorized input arrays in ordered windows capped at min(Environment.ProcessorCount, 4); no public setting.
- [ ] Preserve every physical path check, metadata observation, full content hash and retained native/SDK/parser/analyzer role. For owned physical-only current proofs, grant the complete known inventory once before the callback-free final closure; preserve generic supplied-reader callbacks and initial discovery semantics.
- [ ] Preserve input-index exception precedence including earlier identity mismatch versus later I/O exception; drain all dispatched work before returning or throwing.
- [ ] Preserve original cancellation token, pre-read cancellation, bounded resources and no dispatch after a failing window.
- [ ] Keep arbitrary policy/read delegates and lazy enumerations out of parallel execution.
- [ ] Verify deterministic bound/order/cancellation/draining tests and real authority/path/hash regressions.
- [ ] Demonstrate improvement and unchanged original deadlines under the same combined suite; isolated diagnostic success is not acceptance.

## Evidence

| Current measurement | Value |
|---|---|
| Source | realfork-physical-current-e66a38672e1745fc8ff7e018e42f0b9f |
| Capture calls / inclusive time | 287541 / 69.8 s |
| Hashed open-length sum | 49.73 GB across 283683 captures |
| Hash/read | 32.38 s |
| Physical path / attributes / open / dispose | 20.66 / 6.72 / 7.50 / 1.75 s |
| Status | Instrumented diagnostic only; proposed batch uncompiled and untested. |

## Scope

| Eligible materialized batch | Existing authorization retained |
|---|---|
| Certificate CaptureAsync physical[] | Mandatory documents and dependencyPaths; immutable provenance |
| Certificate CaptureAsync epoch.Inputs | Explicit complete epoch.Mutable policy validation |
| Owned policy-only current and projection proofs | Complete Mutable plus retained parser/analyzer grant; callback-free SDK/native/analyzer/parser/all-epoch/SDK physical closure |
| AssertPhysicalSnapshotCurrentAsync Inputs | Complete Mutable policy validation |
| UnavailableEpoch preparation inputs | Complete input-path policy validation |
| CertifiedEpoch preparation _preparedOutputs | Complete prepared-output policy validation |
| Stage expectation AssertBuildInputsAsync BuildInputs | Complete current grant set plus package proof |

- Keep generic IEnumerable assertion helper sequential; its callers can enumerate lazily.
- Superseding retained-package boundary: request the complete known package grant, then the separate mandatory assets grant, then perform one pure physical check of their complete input union. Keep archive/new-discovery read grants at their actual read boundaries. Do not parallelize policy callbacks.
- Keep producer-assembly checks sequential unless their complete independent grant boundary is separately proven.
- Keep all native evaluation/load bracketing roles and exact issued snapshot binding; no cross-phase cache. Supersede redundant physical-only per-file callback walks only with the proved complete final grant and its existing full physical closure.
- Keep the generic supplied-reader overload and initial parsing/capture discovery unchanged. Unknown newly discovered members invalidate the retained physical receipt before child I/O; this is retained-proof invalidation, not delegated rediscovery.

## Design

- Cap four streams and 256 KiB pooled read buffers per phase, clamped to available CPUs; resource policy, not a claimed optimal throughput constant.
- Validate each captured identity inside its indexed worker, then await/drain the complete window and propagate the first original-index failure.
- At most three later already-authorized paths can be read before an earlier failure is known; failure issues no authority or published result.
- A 24-class suite can create up to 96 streams and 6 MiB pooled buffers; measure contention rather than assuming solo speedup transfers.
- Scratch proposal: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/physical-batch-draft/`.
- Status: proposed; no implementation or performance success claim.

Installed bounded capture helper at eight preauthorized immutable-array positions, with shared identity comparison and all policy/freshness phases retained. physical-batch38 exposed test-only missing namespace/local-name compile errors; corrected. physical-batch39 passed all 32 selected tests, including ten new scheduler/real-capture cases. Guarded sequential mutation de2c5d8f: capacity4 barrier fails, capacity1 control passes, all started work drained. Guarded reversed-failure-order mutation 29b2ae33: exact expected mismatch loses to later IOException and test fails. Both mutations restored exact source bytes. Combined focused40 running; no deadline or full-gate acceptance claimed.

Current-session controlled diagnostics: undo-batch-degree-1-8b128c1d7c4c43b9ac77c6c9e63dc303 and undo-batch-degree-4-b23edfaf49134e998974eeb5d91bbfa4 both passed unchanged two undo rows. Request phases degree1 20.868/17.172 s versus degree4 17.697/16.057 s; degree1 retains Task.Run overhead, so this measures concurrency benefit only. Physical capture count16436 per row unchanged. Fresh cold review found no missing authority/freshness boundary or undrained task. Combined focused40 remains248pass3deadlinefail251; isolated diagnostics do not clear acceptance.

- 2026-10-23: PackageFinalClosure five Windows controls and three cross-platform Linux controls passed; complete package grants precede locked payload reads, mandatory assets refusal retains its original classification, and final callbacks cannot invalidate earlier payloads. Combined unchanged lifecycle deadlines remain red; no throughput completion claim.


2026-10-23 superseding retained-certificate boundary:

| Requirement | Current evidence / remaining work |
|---|---|
| Original first/second Mutable callback multiplicity | Historical design; superseded only for owned physical-only current/projection paths after complete role equivalence review |
| Complete role equivalence | Same SDK/native Membership/analyzer catalog/parser inputs-directories-environment/all authority.Inputs/SDK closure; same mutable-or-nonimmutable grant filtering |
| Supplied delegates | Generic custom-reader overload remains unchanged; no delegate identity tricks or no-op authorizer |
| Denial precedence | causal22 new Windows control independently proves source sharing32/33, then old path reads it before hidden refusal: Denials0/nativeIOException |
| Negative mutation actors | Migrate current/normalization actors to actual complete combined grant; retain actor counts/bytes and selected-input-specific later callback controls |
| Unchanged deadline qualification | Still pending/red; no measured throughput completion claim |

Continuation27 read-only current process diagnostics: testhost256428 causal20 (start03:23local,last failure output03:35) and312936 causal24 (start05:07,last output05:10) remain active; fresh dotnet-stack reports scratch causal20-stacks27.log/causal24-stacks27.log show PhysicalDirectoryScope.AssertCurrentPath, CompilerArgumentParseReceipt.AssertInputsAsync and BindInputReader repeated input-read authorization, plus ForkInvocationAuthority ownership checks on20. This is location evidence only, not measured root-cause attribution, deadlock/env/flake claim, or throughput acceptance. Both original runs retained; source25 frozen queued build still waiting.

Continuation27 independent source rederivation (not implementation/performance acceptance): retained CompilerArgumentParseReceipt.AssertInputsAsync761+ grants known receipt union then repeatedly regrants known singleton kind/file/digest/bound reader. CreateParsingContext483 one productioncaller NormalizeCore237 binds both authorizer/readers; causal24 stack145–169 CurrentBoundary/CaptureAsync/AssertInputs/boundread matches route. Proposed precise scope clarification: keep unknown response/ruleset/reference-search/glob per-observation discovery unchanged, but use explicit product-owned physical parser capability for already materialized retained checks (Session172/194/281/332 and NativeCompilerTaskInputs46): COMPLETE filtered receipt grant then callbackfree existing fullphysical input/directory/environment closure, existing FrozenBoundary before newly discovered childIO. Generic ctor/custom supplied-reader path unchanged; no noopdelegate/delegateidentity/defaultflag/cache or crossphase reuse. Cold independent plan and oldred denial/mutation/directorysentinel tests +same combined unchangeddeadlines required. Stacks do not prove total runtime cause or acceptance.

Current source27 retained parser causal controls passed 26/26 combined with SourceKind/native move after old26 genuine failures. Broader source28 exact EdgeContextAuthority(false,false) hit the unchanged gated 120s request timeout during PhysicalGraphStage.CreateAsync AFTER native Csc intermediate-output/context/document premises passed. Full edge4 run still active at observation; no retry, timeout relaxation, environmental attribution or throughput acceptance. Old source20 EditorConfig aggregate remains active separately; do not conflate revisions.

Completed immutable source28 edge qualification: 4 total, 2 passed divergent-context refusal controls, 2 failed matching-context actual fork Apply at unchanged gated120s deadline, 6m35s. Exact native Csc intermediate-output/context/document premises passed in both failures. Scratch edge28-summary.json and raw TRX retain row outcomes; no unchanged rerun. A later read-only stack attempt found No process with ID111444, so no current stack attribution exists. Wrapper itself reported exit1; an additional outer PowerShell logger syntax mistake is separately recorded, not used as test evidence.

Current-source rederivation identified a correctness mechanism beyond retained parser repetition: PhysicalGraphStage local AssertSourceInputsAsync performs package physical proof before later singleton source policy callbacks, and ContentReplayAuthority.AssertCurrent proves package files before later replay policy callback then omits packages from the final physical pass. A later callback can invalidate already-proven inputs. Correct complete retained grants followed by callback-free full source/package/copy/SDK physical closure; preserve all five Stage phase boundaries, package dependency-versus-assets refusal provenance and actual unknown-path discovery. Plan and deterministic late-mutation/denial regressions required before implementation; source structure is not elapsed-time attribution.
