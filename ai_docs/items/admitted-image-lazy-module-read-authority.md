# admitted-image-lazy-module-read-authority — Authorize admitted cached-manifest modules before lazy metadata IO

**row:** `admitted-image-lazy-module-read-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.Images.cs`
- `tests/RoslynMcp.Tests/AdmittedModuleImageTests.cs`
- `docs/decisions/0029-explicit-preview-snapshot-authority.md`

## Acceptance

- [ ] Compare the admitted manifest metadata with authorized current bytes without invoking Roslyn's linked-module factory; bind linked buffers to the exact admitted manifest AssemblyFile hashes and compare all already materialized native module metadata.
- [ ] Preserve multimodule support, exact per-module current authorization, full metadata/image hashes and module identity; no directory grant or arbitrary reference acceptance.
- [ ] Pin readonly manifest, lazy-state and cached-module accessors to the compiled Roslyn assembly identity and exact field shapes; fail closed on an unsupported contract, with no mutation or ref escape.
- [ ] Observe denied-old-module lazy-factory causal failure and corrected zero-call behavior; dispose all test-created modules and retain ordinary valid multimodule controls.

## Evidence

- Parent causal `restore-native-facts-lazy1.log`: 12 passed / 2 failed / 14 total / 4.39 seconds. The corrected isolated reflection fixture expected factory count zero but observed one; the denied old module was opened. Its undisposed lazy metadata also caused a cleanup failure, now source-corrected with explicit module ownership.
- Official Roslyn AssemblyMetadata.GetOrCreateData selects lazy factory names from `_initialModules[0].GetModuleNames()`, while the old image capture authorized names from current on-disk manifest bytes before GetModules. Public GetModules exposes no manifest-only precheck.
- Production readonly accessor and pre-factory metadata comparison are source-only; fresh parent runtime, current/floor dependency compatibility and complete authority gates remain pending. No completion claim.

Parent restore-lifetime-quoted-old1 causal control: original metadata owner disposal did not dispose the module allocated through its copied lazy metadata; expected ObjectDisposedException was absent. Source correction borrows the original native-owned metadata through pinned GetMetadataNoCopy, validates compiled assembly and exact nonvirtual internal zero-argument Metadata-returning method shape, and never disposes borrowed owners. Complete manifest metadata comparison still precedes GetModules. Workspace/reference/metadata remain alive through awaited capture. Both lazy denied-open and owner-lifetime controls remain; new source is unverified until fresh parent current/floor gates.

## Nonexpanding Issuance Continuation

| Evidence | Result | Contract |
|---|---|---|
| Native full-expansion control | Read returned both exact images; linked file remained locked after scoped owners and one GC | Do not expand borrowed native lazy factories during issuance. |
| restore-native-linked-lazy-skip-diagnostic1.log | Expected image-unavailable primary only; exclusive access to manifest and linked file both true; exact source/test SHA restored | The issuer GetModules call introduced the lasting linked-file lock. |
| Current source | No issuer GetModules call; read-only pinned initial/factory/lazyData/Data.Modules inspection; no cache mutation or borrowed disposal | Manifest hash binds unmaterialized linked buffers; existing native cached metadata remains exact. |
| New controls | Lazy nonexpansion, pre-materialized owner lifetime, populated changed-metadata refusal, real manager multimodule positive and byte drift | Source-only; fresh focused/current/floor gates pending. |

- Supersedes the prior GetModules-based implementation paragraph above; keep this row open until complete validation and merge.

Late-cache continuation: parent guarded reference diagnostic2 executed both LateCachedModule controls against old code. Each passed actual factory-count=1, final linked authorization-count=2 and unchanged disk-byte premises, then failed the required unavailable issuance or typed reader refusal. Source now shares one synchronous cached-native-metadata comparison after every final awaited read and immediately before certificate publication or image return; no factory execution, borrowed disposal or cache mutation. Current exact controls, native multimodule cleanup and SDK-floor gates remain pending; keep acceptance open.

Fresh parent CompilerContext diagnostic16 executed 13 pass / 3 graph failures. Both LateCachedModule controls now pass with actual factory-count=1/final-read-count=2 premises retained, after their old-code causal failures. All seven analyzer ownership boundary controls and all four actual shadow snapshot lifetime controls also pass in that captured DLL. Three remaining failures are static graph duplicate-context matching, owned separately; no complete/current-floor/full-gate or merge claim. Image source stays frozen.

Final-buffer epoch continuation: parent image-close-shutdown-old2 executed all three actual native final-read controls (Source, Assets, Grant). Initial admitted-image read and final-buffer authorization-count=2 premises passed; each then failed because no typed refusal occurred after authored source mutation, selecting-assets byte mutation, or current source grant revocation. Logs image-close-shutdown-old2.log and image-close-shutdown-old.trx also contain two separately owned native cleanup failures (5 failures total), not five image cases. Source correction adds full current epoch/policy validation after returned-buffer reads, explicit cancellation checks, and then the last synchronous cached-native-metadata comparison with no later awaited callback. Issuance already has its owning CompleteAsync post-capture current-policy/full-snapshot/cancellation bracket. Fresh fixed image runtime and complete gates remain pending; all acceptance stays open.
