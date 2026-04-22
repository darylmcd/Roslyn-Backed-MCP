# Test parallelization audit — phased plan (2026-04-22)

<!-- Generated from ai_docs/prompts/backlog-sweep-plan.md v3 conventions. Companion
     state.json lives alongside this file. Targets the 2026-04-19 sweep's
     deferred item `ci-test-parallelization-audit` (P3-UX), which
     `ai_docs/plans/20260419T230057Z_backlog-sweep/state.json` entry #17 left
     open as "Needs its own dedicated sweep. Backlog row stays open." -->

## Premise update (IMPORTANT)

Plan-time source-verify pass revealed that the deferred row's original
framing — *"Adding `[DoNotParallelize]` to 54 SharedWorkspaceTestBase
subclasses exceeds Rule 4's ≤3 test-file cap"* — is **stale as of
2026-04-22**:

```
grep -r -l SharedWorkspaceTestBase tests/ → 56 files
grep -L DoNotParallelize <those 56>      → 0 files
grep -r -l DoNotParallelize tests/       → 72 files
```

Every `SharedWorkspaceTestBase` subclass already carries `[DoNotParallelize]`.
The 72-class opt-out list exceeds the 56 `SharedWorkspaceTestBase` subclass
count — which tells us that the 16 additional opt-outs are on non-shared
fixtures that also had to opt out for other reasons. The attribute-application
work implicit in the row title is **already done**.

The residual open problem is the one surfaced by
`ai_docs/plans/20260419T230057Z_backlog-sweep/state.json` initiative #13's
notes (release-cut skill, PR #322):

> "verify-release.ps1 surfaced 3 pre-existing parallel-cleanup-race flakes
> that match the deferred ci-test-parallelization-audit row — unrelated to
> this doc-only change."

That is: **3 tests flake on `verify-release.ps1` despite having
`[DoNotParallelize]`.** The flakes are named "parallel-cleanup-race" in the PR
notes — strongly suggesting the race is in the **teardown path** (likely
`AssemblyLifecycle.Cleanup` in `tests/RoslynMcp.Tests/AssemblyCleanup.cs`)
rather than in test-to-test class-level parallelism. Possible races include:

- `AssemblyLifecycle.Cleanup`'s `Directory.Delete(tempRoot, recursive: true)`
  running against a directory whose file-handles are still held by a just-
  finishing test-fixture build-host subprocess.
- `TestBase.DisposeAssemblyResources`'s `WorkspaceManager?.Dispose()` racing
  with a file-watcher callback that fires after the manager is nulled.
- `TestFixtureFileSystem.CreateSampleSolutionCopy` copies using
  `Guid.NewGuid()` subdirectories under `tempRoot` — if two classes call it
  during teardown (unlikely but possible), they contend on the same parent.

This plan reshapes the deferred row's original scope: **triage the flakes,
fix the actual race, validate under concurrent load.** The per-class
attribute audit is downgraded to an *optional* Phase 4 that only runs if
Phase 1's triage finds opt-outs that can be safely removed (i.e., classes
that were opted out defensively rather than for a real shared-state reason).

## Inventory

- P3-UX: 1 row (`ci-test-parallelization-audit`) — currently NOT present in
  `ai_docs/backlog.md` as of 2026-04-22T11:48Z (`updated_at`). State-file
  audit shows the row was deferred via the 2026-04-19 sweep's state.json
  (status: `deferred`; `backlogRowsClosed: []`) but appears to have been
  removed from `backlog.md` during the sweep-close reconcile. Phase 1 of
  this plan includes a `backlog.md` restoration step if the row is genuinely
  missing; otherwise the plan closes-or-updates the existing row as written.

Blocker / deps: none.

Actionable initiatives this sweep: **4** (3 primary + 1 optional Phase 4
contingent on Phase 1 findings).

## Anchor verification (Step 3 of the planner prompt)

Plan-time source reads, all present as of 2026-04-22T17:05Z:

- `tests/RoslynMcp.Tests/AssemblyInfo.cs:8` —
  `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]`.
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs:4-27` — `AssemblyLifecycle.Cleanup`
  calls `TestBase.DisposeAssemblyResources()` then deletes `tempRoot`
  best-effort.
- `tests/RoslynMcp.Tests/TestBase.cs:200-221` —
  `DisposeAssemblyResources` acquires `_initLock`, calls
  `WorkspaceManager?.Dispose()`, catches all exceptions silently, clears
  `_workspaceIdCache`, sets `_servicesInitialized = false`.
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs:9` —
  `tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests",
  Guid.NewGuid().ToString("N"))`. Each call produces a distinct subfolder,
  so cross-class contention on distinct test-classes' temp copies is
  unlikely; single-shared-`tempRoot` contention in `Cleanup` is possible.

## Sort order

Strictly sequential. Phase 1 (triage) must produce the diagnostic that
informs Phase 2's fix. Phase 3 adds a regression gate. Phase 4 is optional
and contingent on Phase 1 findings.

## Scope-discipline self-vet

- [x] No initiative closes more than 1 row; only Phase 3 (or Phase 4 if it
      runs) closes `ci-test-parallelization-audit`.
- [x] No initiative touches more than 4 production files (Rule 3).
- [x] No initiative adds more than 3 test files (Rule 4). Phase 2 adds 1
      regression test; Phase 3 adds 0-1; Phase 4 (optional) cites a Rule 4
      exemption for attribute-only edits (no new test methods).
- [x] Every initiative has `estimatedContextTokens` ≤ 80K. Max is Phase 2
      at 40K.
- [x] Every initiative has `toolPolicy: edit-only`.
- [x] Single P3 row in scope — Phase 1 leads off as the only investigate
      step; the "in top 5 by sort order" rule is trivially satisfied.

## Initiatives

---

### 1. `test-parallel-audit-phase1-triage` — Reproduce the 3 flakes + diagnose root cause

**Status:** pending · **Order:** 1 · **Correctness class:** P3-UX · **Schedule hint:** — · **Estimated context:** 35000 tokens · **CHANGELOG category:** Maintenance

| Field | Content |
|---|---|
| Backlog rows closed | (none — sets up Phase 2) |
| Diagnosis | PR #322's merge notes cite "3 pre-existing parallel-cleanup-race flakes" but do not name them. We do not know (a) which 3 tests flake, (b) whether the race is in `AssemblyLifecycle.Cleanup`, in per-test disposal, or in a specific fixture's temp-file teardown, (c) whether `ExecutionScope.ClassLevel` parallelism contributes or whether the race is purely in teardown. Phase 2 cannot write a targeted fix without this information. |
| Approach | (a) Run `eng/verify-release.ps1` 10× in a loop locally (or in a dedicated `gh workflow run verify` dispatch) and collect per-run test logs to `ai_docs/reports/test-parallelization-triage-2026-04-22.md`. (b) For each flake observed, capture: test class + method name, failure stack, whether the fixture uses `CreateSampleSolutionCopy` or the shared `SampleSolutionPath`, whether the test acquires `WorkspaceManager` via `GetOrLoadWorkspaceIdAsync` or constructs its own. (c) Cross-reference failure stacks against `AssemblyCleanup.cs:10-26` (tempRoot deletion) and `TestBase.cs:200-221` (`DisposeAssemblyResources`) to localize the race. (d) Classify each flake as one of: `cleanup-teardown-race` (e.g., tempRoot deletion racing active fixture I/O), `workspacemanager-dispose-race` (file-watcher callback after dispose), `fixture-copy-race` (two classes concurrently calling `CreateSampleSolutionCopy`), or `other` (requires deeper analysis). (e) Write the triage report with enough detail that Phase 2 can write a focused fix without re-running the investigation. |
| Scope | prod: 0. tests: 0 new. docs: 1 new (`ai_docs/reports/test-parallelization-triage-2026-04-22.md`). |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 tokens |
| Risks | (a) Flakes may be low-frequency — a 10-run loop may not reproduce all 3. Mitigation: if < 3 reproductions in 10 runs, escalate to 25 runs and cite the frequency in the report. (b) The report may conclude the race is elsewhere than expected — Phase 2's brief is deliberately generic ("fix the race Phase 1 identifies") so it can absorb any of the four race-shape classifications. (c) If `ci-test-parallelization-audit` row is genuinely missing from `ai_docs/backlog.md`: Phase 1 also re-adds it with the updated post-verify framing (3 named flakes + fix approach) so the executor chain downstream has an explicit row to close. |
| Validation | Triage report exists, classifies each observed flake by race-shape, cites file:line evidence for the suspected race, and concludes with a "Phase 2 fix directive" one-liner. |
| Performance review | N/A — investigation phase. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | **Maintenance:** Triage report for 3 `verify-release.ps1` flakes surfaced in PR #322 landing checks. Classifies each as teardown-race / dispose-race / fixture-copy-race / other and points Phase 2 at a specific code-path fix (`ci-test-parallelization-audit`). |
| Backlog sync | No row closed yet. If the row was removed from `ai_docs/backlog.md` during the 2026-04-19 sweep close, restore it with the updated phrasing so Phase 3 has a row to close. |

---

### 2. `test-parallel-audit-phase2-fix-race` — Fix the teardown race identified by Phase 1

**Status:** pending · **Order:** 2 · **Correctness class:** P3-UX · **Schedule hint:** — · **Estimated context:** 40000 tokens · **CHANGELOG category:** Fixed

| Field | Content |
|---|---|
| Backlog rows closed | (none — Phase 3 closes) |
| Diagnosis | Phase 1's triage report identifies the specific race (most likely `AssemblyLifecycle.Cleanup`'s `tempRoot` deletion racing a file-watcher still draining, or `WorkspaceManager.Dispose` not awaiting in-flight load/reload operations). This phase implements the fix against the localized code path. |
| Approach | Contingent on Phase 1's findings. Most likely shape: (a) In `AssemblyLifecycle.Cleanup`, await `WorkspaceManager.DisposeAsync()` (introduce the async disposal if not present) before deleting `tempRoot`. (b) In `TestBase.DisposeAssemblyResources`, serialize the dispose behind `_initLock` which is already acquired; add an explicit "drain file-watchers" step before `_servicesInitialized = false`. (c) Add one new regression test `tests/RoslynMcp.Tests/AssemblyTeardownRaceTests.cs` that constructs a throwaway `TestBase`-like fixture, kicks off a file-watcher event, and asserts `Dispose` blocks until the callback drains. Run the test 100× in tight loop in CI (not just once) to guard against rerelapse. **Fallback shapes** if Phase 1 finds a different race: adjust the file surgery to whichever 1-2 prod files own the actual race (e.g., `WorkspaceManager.cs` for dispose-ordering, `FileWatcherService.cs` for callback draining). The prod-file cap is 2 in all cases. |
| Scope | prod: 1-2 (whichever files Phase 1 localizes; most likely `tests/RoslynMcp.Tests/AssemblyCleanup.cs` + `tests/RoslynMcp.Tests/TestBase.cs`, OR `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` if the race is server-side). tests: 1 new regression (`AssemblyTeardownRaceTests.cs` or an equivalent name Phase 1 recommends). |
| Tool policy | `edit-only` |
| Estimated context cost | 40000 tokens |
| Risks | (a) Adding async-disposal to `AssemblyLifecycle.Cleanup` may interact with MSTest's `[AssemblyCleanup]` method-signature constraints — verify the attribute supports `Task`-returning async cleanup (MSTest 3.x does via `[AssemblyCleanupAttribute]` on an async method; plan-time assumption, verify at execution time). (b) The regression test must be stable in tight loops — use explicit `TaskCompletionSource` + `Task.WhenAll` rather than `Task.Delay`-based synchronization so the test doesn't become a flake source itself. |
| Validation | New regression test green across 100 tight-loop runs (`for /L %%i in (1,1,100) do dotnet test --filter AssemblyTeardownRace`). `verify-release.ps1` green across 3 consecutive runs (pre-Phase 3 gate). No new `[DoNotParallelize]` attributes introduced (attribute count unchanged: 72). |
| Performance review | N/A — teardown-path fix; test-suite wall-clock unchanged. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** Test-assembly teardown no longer races with lingering file-watcher callbacks (or [Phase 1's actual root-cause]) — `AssemblyLifecycle.Cleanup` now awaits async disposal before deleting `tempRoot`. Eliminates the 3 `verify-release.ps1` flakes documented in `ai_docs/reports/test-parallelization-triage-2026-04-22.md` (`ci-test-parallelization-audit`). |
| Backlog sync | No row closed yet. |

---

### 3. `test-parallel-audit-phase3-regression-gate` — Add concurrent-load regression gate + close row

**Status:** pending · **Order:** 3 · **Correctness class:** P3-UX · **Schedule hint:** — · **Estimated context:** 35000 tokens · **CHANGELOG category:** Added

| Field | Content |
|---|---|
| Backlog rows closed | `ci-test-parallelization-audit` |
| Diagnosis | The 3 Phase-1 flakes are pre-existing and escaped every prior sweep because `verify-release.ps1` ran once per PR and one clean run hides N flaky runs. A regression gate that runs the test suite K times under concurrent load and asserts zero new failures is the right second-order defense: if Phase 2's fix regresses or a new teardown race is introduced, the gate fails at PR time instead of at release-cut time. |
| Approach | (a) Add `eng/verify-release-stress.ps1` that runs `eng/verify-release.ps1` K=3 times back-to-back and fails if any run has a non-zero exit. Parameterized so K can be increased. (b) Wire into `.github/workflows/ci.yml` as a new `stress` job that runs on `push` to main + scheduled weekly; do NOT run on every PR (CI minutes budget; Phase 1 + Phase 2 together already provide PR-time coverage via the single-run verify). (c) Update `CI_POLICY.md` to document the stress gate and its cadence. (d) Update `ai_docs/backlog.md` to delete `ci-test-parallelization-audit` row (or if absent, confirm it stays absent). Bump `updated_at`. |
| Scope | prod: 3 (`eng/verify-release-stress.ps1` new + `.github/workflows/ci.yml` edit + `CI_POLICY.md` edit). tests: 0 new (the "test" is CI configuration, not a C# test). docs: `ai_docs/backlog.md` row removal. |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 tokens |
| Risks | (a) The stress job at K=3 roughly triples CI wall-clock for the run that triggers it — acceptable on scheduled weekly / push-to-main cadence; unacceptable on every PR. Ensure the `.github/workflows/ci.yml` gating conditions actually scope to main-only + scheduled. (b) If Phase 2's fix was insufficient, the stress job will fire red post-merge — accept this as signal; follow up with a new plan cycle. |
| Validation | Manually trigger `gh workflow run stress` (or equivalent dispatch); confirm all 3 iterations green. `ai_docs/backlog.md` no longer lists `ci-test-parallelization-audit`; `updated_at` reflects the close. |
| Performance review | N/A — CI infra change. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | **Added:** `eng/verify-release-stress.ps1` + a scheduled-weekly / push-to-main CI job that runs the full test suite 3× back-to-back and fails on any iteration failure. Guards against regression of the Phase 2 teardown-race fix and against future parallel-cleanup races. Closes `ci-test-parallelization-audit`. |
| Backlog sync | Close row: `ci-test-parallelization-audit`. |

---

### 4. `test-parallel-audit-phase4-optional-prune-redundant-opt-outs` *(optional — runs only if Phase 1 finds safe candidates)*

**Status:** pending (conditional) · **Order:** 4 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 45000 tokens · **CHANGELOG category:** Changed

| Field | Content |
|---|---|
| Backlog rows closed | (Phase 3 already closed the primary row; this phase is a follow-up perf win) |
| Diagnosis | The 2026-04-22 plan-time audit found 72 classes with `[DoNotParallelize]` against only 56 `SharedWorkspaceTestBase` subclasses — so 16 classes have opted out for reasons other than shared `TestBase` static state. Phase 1's triage may identify some of those 16 as defensively-opted-out (no real shared state; attribute was added prophylactically). If so, removing those attributes recovers CI-time parallelism without reintroducing flakes. This phase is **skipped entirely** if Phase 1 finds zero safe candidates — do not force it for the sake of completeness. |
| Approach | (a) From Phase 1's triage report, list the N test classes whose shared-state profile is purely per-test (no `TestBase` static services, no tempRoot sharing, no WorkspaceManager use). (b) For each class, remove the `[DoNotParallelize]` attribute and the adjacent "`// opt-out reason: …`" comment if present. (c) Apply in **batches of ≤ 10 classes per PR** under an explicit Rule 4 exemption: attribute-only deletions do not add new test methods and do not "extend" the files in the Rule-4 sense (the "adds ≤ 3 new test files OR extends ≤ 3 with new test methods" phrasing is about coverage additions, not annotation cleanup). Cite the exemption in each PR description and in the initiative's notes. (d) After each batch, run the Phase 3 stress gate (K=3 verify-release.ps1 runs) before the next batch ships. |
| Scope | prod: 0. tests: 10 files per batch × however many batches Phase 1 identified. **Rule 4 exemption cited** — attribute-only deletions without new coverage. If the exemption is rejected at vet time, split into batches of ≤ 3 files/phase and extend this plan with the additional phases. |
| Tool policy | `edit-only` |
| Estimated context cost | 45000 tokens (per batch; if Phase 1 finds 16 candidates → 2 batches → 90K total which exceeds Rule 5 — split across 2 separate sweep entries in that case) |
| Risks | (a) The biggest risk is reintroducing flakes. Mitigation: the Phase 3 stress gate runs on every batch ship; any regression surfaces within 1 push-to-main cycle. (b) Rule 4 exemption may be rejected by the executor's vetting step — if so, split per the "If the exemption is rejected at vet time" clause above. |
| Validation | Per-batch stress gate green (K=3 verify-release.ps1 iterations). Before/after CI wall-clock comparison: the expected win is small (tens of seconds) unless many classes were defensively opted out. |
| Performance review | Recovers CI parallelism in the pruned classes; measurable but likely small (< 30s CI wall-clock). |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** Removed `[DoNotParallelize]` from N test classes that were opted out defensively but have no real shared-state requirement. CI class-level parallelism now covers these classes; the Phase 3 stress gate guards against regression. Follow-up to `ci-test-parallelization-audit`. |
| Backlog sync | None (primary row already closed by Phase 3; this is a follow-up perf win). |

---

## Final todo

- [ ] `backlog: sync ai_docs/backlog.md` — Phase 3 deletes `ci-test-parallelization-audit` (or confirms it stays absent if the 2026-04-19 sweep close already removed it) and bumps `updated_at`. If Phase 1 found the row missing, Phase 1 re-adds it with the post-verify phrasing so Phase 3 has a row to close.

## Suggested kickoff order

Strictly sequential. Phase 1 → Phase 2 → Phase 3 is the primary path. Phase 4
runs only if Phase 1 flags candidates; otherwise the plan completes at
Phase 3 and Phase 4's state.json entry is marked `status: obsolete` with
`notes: "Phase 1 found no safe opt-out removal candidates."`
