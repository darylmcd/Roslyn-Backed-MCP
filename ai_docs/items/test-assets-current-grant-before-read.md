# test-assets-current-grant-before-read — Renew restore read grants after policy callbacks

**row:** `test-assets-current-grant-before-read` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`: selector reads, protected-input sweeps, captured custom-package condition grants.
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`: combined capture and aliased-document fallback.
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs`: initial/rebuilt planning, copying and definition reads.
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`: directory enumeration after build-receipt callbacks.
- `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`: PhysicalGraphExpectation input hashing after package callbacks.
- `tests/RoslynMcp.Tests/TestRunnerEvaluationPropertiesTests.cs`: selector/protected-input/custom-package callback regressions.
- `tests/RoslynMcp.Tests/NativeCertificateCurrentGrantTests.cs`: mandatory revocation and degraded-loading controls.
- `tests/RoslynMcp.Tests/PhysicalGraphStageTests.cs`: planner, copy, definition and issued-receipt regressions.

## Acceptance

- [ ] Retain complete assets preflight and obtain a fresh exact current grant immediately before each assets filesystem inspection/read.
- [ ] Revoke a later selector during an earlier selector policy callback; refuse before attempting the later locked content read.
- [ ] Keep stable two-selector preparation successful with both exact assets identities and exactly one restore per selected project.
- [ ] Observe causal failure before the repair and pass afterward; retain all package/manifest/held-input authority checks.

- [ ] Renew exact mutable read/enumeration/copy sets after intervening policy callbacks; preserve SDK/bootstrap exemptions and all preflights.
- [ ] Carry captured custom-package condition targets requiring current grants into protected-input and execution identities.
- [ ] Preserve mandatory-denial propagation and semantic-only dependency/alias degradation.
- [ ] Keep stage and fork planner refusals before sentinel enrollment; independently remove only the fork renewal to prove its control.

## Evidence

- Current loop validates the batch once, then awaits package proof callbacks between assets reads. The later selector read currently precedes the next per-selector CreateAsync authorization.
- This is an exposed current-grant ordering defect in the restore prerequisite, to fix before shipping.

Qualification196-200: corrected two-project phase barrier proves selector revocation reaches locked content on old code (196: one intended failure, one stable pass); fresh selector grant passes whole test-runner class197 (66). Same callback-interleaving mechanism also affects both AssertRestoreInputsCurrent protected read sweeps and NativeCompilerContextCertificate combined capture/aliased-document fallback. Tests-first199: four intended locked-read failures, five stable/degraded controls pass. Fresh exact grants installed;200 all11 controls pass. Stage/fork planning, copy and definition-read boundaries remain under causal qualification; see session scratch current-grant-callback-read-boundaries.md. Keep row open until complete mechanism and full restore gates pass.

Qualification202-204: expanded same-mechanism repair covers PhysicalGraphStage planning/copy/definition reads, ForkGraphPreparation planning, PhysicalGraphExpectation build-input reads, and captured custom-package condition targets in TestRunnerService.NativeProtocols. Tests-first202: 15 total, four prior controls pass and eleven intended failures (seven stage/fork/receipt boundary cases; two revoked package-target reads; two missing retained marker identities). Fixed203: all22 combined grant controls pass. Fork-only mutant204 retains every other fix and fails exact sentinel-enrollment assertion; independent root-not-in-build-read-set premise passes, exact source SHA restored. Five production files and three test files are the forcing complete callback-interleaving scope. No deadline repair or full-gate completion claimed; keep open.

2026-10-17 qualification211: normal Release rebuilt source passed all 60 focused current-grant/native-coordinate/native-truth/preview tests on Windows and byte-verified identical assemblies on Linux SDK10.0.400; zero failures/skips on both. Includes strengthened fork planner premise after independent204 mutant. Receipts: scratch/roslyn-prerequisites-20261016-resume/qualification211/qualification.trx and qualification211-linux/results.trx/copy.sha256. Full gate pending.
