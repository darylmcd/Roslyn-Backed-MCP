# fixture-restore-process-lifetime — Bound fixture restore process ownership

**row:** `fixture-restore-process-lifetime` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs:31-40,68-83` — optional caller cancellation defaults to None; shared restore forwards it directly.
- `tests/RoslynMcp.Tests/NativeCompilerContextCertificateTests.cs:235-236` — direct native fixture restore explicitly uses None.
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:120-139` — process cancellation is caller-owned; WaitForExitAsync has no independent deadline; post-exit pipe drain is already bounded.

## Acceptance

- [ ] Inventory shared and direct test-fixture restore subprocess callers; route the same unbounded setup mechanism through a fixture-owned lifecycle boundary. Keep production request timing contracts unchanged.
- [ ] Establish a finite fixture setup deadline from measured normal restore behavior; retain caller cancellation separately and identify the timer that fired. Never increase existing request/test assertions to accommodate setup.
- [ ] On deadline/cancellation, await owned child-tree termination and output drain; preserve primary failure plus cleanup failures, nonzero exit/output evidence, literal argument semantics, and fixture-directory ownership.
- [ ] Demonstrate old failure with a deterministic non-exiting owned child, then success of bounded failure/cleanup; cover caller cancellation, nonzero exit, normal restore, and inherited output pipes. Confirm no live descendants or retained workspace/temp state.
- [ ] Execute real native restore fixture controls and required local/hosted gates. Do not use repeated green retries as causal evidence.

## Evidence and limits

| Evidence | Observation |
|---|---|
| Source inspection 2026-10-17 | Default/shared and explicit/direct fixture restore calls can wait indefinitely if the child never exits and no caller cancellation arrives. |
| qualification229 | Full 58-case WorkspaceForkGraphReplayTests class selected; many serial native operations plausibly explain long runtime. No claim that a restore currently hangs or that this gap caused elapsed runtime. |
| Scope | Separate fixture lifecycle defect; no implementation or running-gate limit changes authorized by this filing. |

## Duplicate check

- Searched current backlog index and `ai_docs/items/*.md` for fixture/restore deadline, unbounded subprocess and lifecycle terms.
- `fixture-git-add-hosted-deadline` concerns an existing bounded Git staging deadline and managed scheduling; it does not cover absent fixture restore deadlines.
- `gate-timing-tests-fail-under-machine-load` concerns validation/Git/pwsh timing observations; it does not own this missing restore lifecycle boundary.
- `restore-callers-missing-packages-path` owns production restore authority/budgets; preserve that separate contract.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.

2026-10-23 verified direct same-mechanism caller MixedRestoreDocumentAuthorityTests.cs:43-46 invokes actual DotnetCommandRunner restore with CancellationToken.None; preserve real generated props replacement and package/action authority assertions when migrating to existing owned fixture restore boundary. DownloadedPackageAuthorityTests direct fixture restore also remains in inventory; cleanup alone does not repair process ownership.

2026-10-23 direct actual restore None also confirmed PhysicalGraphAssetsOverlayTests.cs:37. Preserve dual installation/selecting epoch authority assertions; no running-gate duration claim follows from this source defect.

2026-10-23 direct same mechanism confirmed NativeCompilerReferenceContextTests.cs:53-54 actual restore None; NativeCompilerTaskContextTests.cs:58-60 independent actual msbuild None. Inventory includes both actual native producers; no weakened oracle/timeout or unmeasured hang claim.

2026-10-23 exact additional real restore None callers: RestorePathSlotNativeTests.cs:55-56; RetainedPackageReplayAuthorityTests.cs:29. Current cleanup does not qualify finite fixture process ownership.

2026-10-23 WorkspaceSessionLoaderAdmissionTests.cs:60-61 direct actual restore uses None; finite process lifetime remains separate from current primary cleanup.

2026-10-23 PhysicalGraphContentReplayTests.cs actual restore39-40/118-119 uses None; tracked separately from primary fixture cleanup.

2026-10-23 additional native producer None callers observed: graph replay fixture PrepareNativeMultiTarget122, WorkspaceForkGraphReplayTests110/145; NativeToolsVersionTests native msbuild39; PhysicalGraphCleanupAuthority/PhysicalGraphMembership/ForkFailureRedaction/LocalPackageFork native setup producers. Inventory actual native commands beyond restore without relaxing existing deadlines; exact source paths to reconcile before repair.
