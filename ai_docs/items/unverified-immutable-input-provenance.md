# unverified-immutable-input-provenance — Verify readonly tool and package provenance before exempting current path policy

**row:** `unverified-immutable-input-provenance` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/MsBuildInitializer.cs`
- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs`
- `src/RoslynMcp.Roslyn/Services/RestoredPackageManifest.cs`
- `src/RoslynMcp.Roslyn/Services/RestoredCompilerInputAuthority.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`
- `tests/RoslynMcp.Tests/InstalledPackageAuthorityTests.cs`
- `tests/RoslynMcp.Tests/Helpers/NuGetInstallationFixture.cs`

- `src/RoslynMcp.Roslyn/Services/BuildInputProvenance.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs`
- `src/RoslynMcp.Roslyn/Services/EvaluatedProjectGraph.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphLayout.cs`
- `tests/RoslynMcp.Tests/ImmutableInputAuthorityTests.cs`
- `tests/RoslynMcp.Tests/BuildInputProvenanceTests.cs`
- `tests/RoslynMcp.Tests/Helpers/OwnedNativeCompilerInputContext.cs`
- `src/RoslynMcp.Roslyn/Services/SupplementalPackageReadAuthority.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `tests/RoslynMcp.Tests/SupplementalPackageReadAuthorityTests.cs`

## Acceptance

- [ ] Project/global overrides of tool-root properties cannot exempt unrelated files from current path policy; independently bind SDK/runtime roots to the registered bootstrap/runtime provenance.
- [ ] Package immutable authority binds exact package ID/version/library path and validated installed package manifest; arbitrary assets JSON cannot mint unrelated subtree authority.
- [ ] Preserve current authorization for authored/mutable inputs and every newly discovered recursive child; proven immutable inputs retain exact physical identity and byte epochs without requiring broad mutable grants.
- [ ] Observe forged tool/package causal negatives and real SDK/installed-package positives; current-policy denial happens before explicit read/copy.

## Evidence

- 2026-10-16 source review: ToolRoots trusts evaluated MSBuildToolsPath/MSBuildSDKsPath/NetCoreRoot/NetCoreTargetingPackRoot. ReadPackageRoots trusts any relative library.path beneath packageFolders without installed package identity validation.
- Parent directed tests-first regression coverage in ImmutableInputAuthorityTests; runtime and complete provenance correction pending. This is a required active restore companion, not a deferred limitation.

Parent actual old-behavior evidence: restore-immutable-provenance-old1 compiled and ran5 cases: empty-property current-policy control passed; both project-controlled NetCoreRoot and NetCoreTargetingPackRoot bypassed denied artifact authorization; unrelated and canonical-looking unverified package paths minted immutable roots without required refusal. Four causal failures observed in872ms. Approved implementation direction: independently captured pre-evaluation SDK/runtime/cache provenance or current exact root grants before any package-manifest IO; privately issued validated installation/file proof required at all consumers, including RestoredPackageManifest. Current source migration is unverified; no shipping/completion claim.

2026-10-16 parent causal old gate: 1 control passed / 4 forged provenance cases failed. Fresh checkpoint package/provenance gate: 17 passed / 2 failed; genuine signed package exposed separate archive-hash versus unsigned-content-hash validation, corrected using official NuGet APIs pending fresh runtime. The other failure remains native condition provenance under active sibling ownership. All acceptance remains unchecked; current/floor SDK runtime, source epoch and complete lifecycle validation remain required.

Parent source review exposed unavailable-epoch early returns before mandatory authored Compile authorization: invalid assets, missing assets and denied package grant skip sourceDocuments approval. Tests-first NativeMandatorySourceAuthorityTests covers all three states with both authored and external Compile inputs. The source/project/assets/CLI boundary remains mandatory; early-return production is intentionally unchanged pending parent causal runtime and complete correction. Analyzer-owned IO is tracked separately as analyzer-shadow-copy-input-authority.

2026-10-16 full SampleSolution KeepSuccess native fork actual guarded causal FAIL after ReviewedGraph: rejected input role PackageInput=True, RequiresCurrentGrant=False, AssetInput=False, ConditionInput=False, LayoutInput=False, Immutable=True. Exact source restoration receipt fork-preparation-phases-a9a4ad27560043f6972497e5c5844367 validates both instrumented files restored. Fork preflight blanket mutable approval was inconsistent with already-issued package proof. Source fix reuses PhysicalGraphExpectation.AssertBuildInputsAsync for both pre-copy/final-source checks, binds original mutation prebytes, and retains mutable layout inventory approval. Fixed runtime pending; current-grant custom-cache refusal and package/assets drift controls remain required. Stage phase profile: baseline native load about39.5s and final native load about33.2s; earlier120s full-fork timeout is unresolved and must not be called environmental or covered by small cleanup fixtures.

2026-10-16 scoped package transfer checkpoint (parent-run; row OPEN):

| Boundary | Observed evidence / remaining obligation |
|---|---|
| Original package source checks | Shared expectation correction: installed-package controls8 + small native fork cleanup2 passed. Full SampleSolution advanced beyond the earlier blanket preflight package denial. |
| Missing private assets | Guarded loader profile b937888f68d9461389b6b0b1568ea9a6 failed initial fork epoch capture: denied role BootstrapPackage=True, RegisteredTool=False, Other file role. No selecting assets were synthesized; all four instrumented sources restored exactly. |
| Scoped supplemental issuer | Required manager-private capability uses only the attached original certified package proof, original session/loaded graph/version and original workspace policy scope. Original-scope current/revoke/close/reload/package/assets controls6 and real inherited-package missing-assets control1 passed in the parent 27-case gate. These seven passes do not establish full SampleSolution success. |
| Retained imports without authored PackageReference | Actual restored source retained package assets/imports after authored reference removal. Source package-document premise passed; private missing-assets load causally failed original-cache grant scope while retained-reference control passed (package-no-reference-final-old2). Branch correction is source-only pending fresh verification. |
| Final manager publication | Guarded omission of ONLY pre-Publish supplemental assertion: real base loader completed, original source closed, private workspace incorrectly published. New (True,True) row failed expected specific ownership refusal/no exception; remaining capability8 and ordinary no-package None2 passed. Exact source restoration verified by manager-publication-old-eff435c889de4692ba5a77a996f4df03/receipt.json. Separate final-compiler released-control failure had an invalid independent native prerequisite, not a second causal publication failure. Restored check needs fresh fixed runtime. |
| Performance | Deep stage preflight measured113.930s, including baseline producer about17.8s and baseline/desired normalization about8s each. Earlier120s timeout remains unresolved. Remove only source-proven duplicate checks between immutable operations; preserve every actual command/read/publication boundary. |
| Completion | Full SampleSolution native fork, fresh fixed capability/publication controls, aggregate/hosted gates and reviewed delivery remain required. No closure or full-head verification claim. |

| 2026-10-16 bounded checkpoint | Evidence | Limit |
|---|---|---|
| Partial restore sequencing | RestoreOutputEpoch9 passed; actual full Sample KeepSuccess passed1/1 in4m07 (restore-fork-output-epoch-fixed1/fork-output-epoch-fixed.trx). | The successful snapshot preceded invocation-scope migration; earlier120s failures are historical observations, not an environment disposition. |
| Scoped current grants | Fresh first migrated snapshot compiled; focused82 passed0skip in2m22, including manager authority9, wrapper9, Supplemental11 and nativecleanup2. | Real ordinary-project fork exercised validation; restrictive-grant test-process execution still requires its own real test-project control. No aggregate/merge/acceptance closure. |
| Session lifecycle companions | Distinct OPEN rows workspace-load-registration-compensation and workspace-native-load-cleanup-gate-release track registration rollback and disposal/gate release. | Native old-behavior guards pending; do not count new source-only correction as old causal evidence. |
