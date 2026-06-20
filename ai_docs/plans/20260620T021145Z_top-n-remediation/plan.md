# Top-N Remediation Session Plan — 20260620T021145Z

**Invocation:** `/top-n-remediation` (no args) → N=5, autonomous (no `gate=ask`), no `budget-minutes`, no `resume`, no `contextPct`.
**Backlog snapshot:** `ai_docs/backlog.md` `updated_at: 2026-06-19T21:59:45Z` (35 open rows).
**Preflight:** `main` clean + synced; no rebase/merge; `gh` authed (`repo` scope); coreutils on PATH. Orphan-PR sweep: 5 open PRs (#981–985) all Dependabot NuGet bumps — **no backlog-id PRs**, exclusion set empty. Plan-collision: `20260618T143921Z_backlog-sweep` all 8 initiatives `merged`/terminal → `OWNED_BY_ACTIVE_PLAN` empty.

## Selection

| id | rank | reasons | estimated file touches |
|----|------|---------|------------------------|
| `filewatcher-waitforstale-clearstale-stranded-awaiter` | 1 | Medium, shovel-ready S bug (0/0 signals). `ClearStale` swaps `_staleSignal` TCS without completing it → awaiter stranded to its CT deadline. Latent prod trap. | 2 (`FileWatcherService.cs` + staleness test) |
| `ci-vuln-gate-format-json-hardening` | 2 | Medium, shovel-ready S ci (0/0). Blocking vuln gate keys on English substring → locale/SDK-wording drift silently fails-open. Switch to `--format json`. | 1 (`.github/workflows/ci.yml`) |
| `publish-nuget-verify-package-types` | 3 | Medium, shovel-ready S ci (0/0). Verify step only size-checks nupkg; a csproj regression dropping `<PackageType>McpServer</PackageType>` / `.mcp/server.json` ships a tool-only pkg on green CI. | 1–2 (`publish-nuget.yml`; reads csproj) |
| `nuget-version-checker-timeout-test-wallclock-race` | 4 | Medium, shovel-ready S test (0/0). Release-gating flake **recurrence**: helper's 5 s wall-clock `WaitAsync` races checker's internal timeout. Root-cause = event-driven wait, not constant bump. | 1 (`NuGetVersionCheckerTests.cs`) |
| `dependabot-groups-batching` | 5 | Low, shovel-ready S ci (0/0). No `groups:` → every minor/patch bump = own PR. Add per-ecosystem grouping; keep majors individual. | 1 (`.github/dependabot.yml`) |

## Per-row state

- `filewatcher-waitforstale-clearstale-stranded-awaiter`: landed (PR #994 squash-merged → main ef6aad6; CI validate PASS 15m37s; branch+worktree clean)
- `ci-vuln-gate-format-json-hardening`: landed (PR #995 squash-merged → main 08d8c46; CI validate PASS 14m39s, new JSON gate ran live; clean)
- `publish-nuget-verify-package-types`: landed (PR #996 squash-merged → main 87c16ef; CI validate PASS 13m29s; clean)
- `nuget-version-checker-timeout-test-wallclock-race`: landed (PR #997 squash-merged → main 0e989d0; CI validate PASS 14m5s, de-flaked test passed; clean)
- `dependabot-groups-batching`: landed (PR #998 squash-merged → main 13420d2; CI validate PASS 11m40s; clean)

States: `selected → implemented → reviewed → pr-open → landed`; terminal `skipped:<why>` / `ship-failed:<why>`.

## Ship strategy

**Immediate-land per row, rank order 1→5** (ship-loop §Autonomous batch policy, default mode). Rationale: rows touch fully disjoint files (no conflict-ordering benefit from batch-land); single self-hosted runner serializes CI regardless; immediate-land keeps the runner idle during each row's local impl work (honors the no-double-load constraint) and cuts each branch from a fresh `main`.

**Validation:** light targeted local checks per row (Roslyn `compile_check` + targeted `test_run --filter` for C# rows; YAML logic review for workflow rows) — **the PR CI on the self-hosted runner is the authoritative gate** (`watch-pr` to green before merge). Full `verify-release.ps1` is NOT run locally (single-runner no-double-load).

**Batch-aware expected-reds:** row #4 fixes a known transient flake (`NuGetVersionCheckerTests.GetLatestVersion_OnTimeout_...`) that runs in `verify-release.ps1` and gates every PR. Until #4 lands, it may transiently redden siblings' CI → classify as **known-flake**, re-run, do not treat as a real red. No other cross-row reds (disjoint files).

**Excluded from autonomous batch (surfaced for operator):** `parameter-naming-canonicalization-migration` and `apply-composite-preview-destructive-misnomer` are size-safe but BREAKING published-surface (Directive #4 → ADR + migration note + operator timing).

## Spillover backlog rows to file (Directive #3 — file in session-end docs PR)

Discovered while implementing selected rows; kept out of per-row PRs to preserve scope.

- **(from row 1)** `filewatcher-watcherentry-watchers-unguarded-mutation` — Low/S: `WatcherEntry.AddWatcher` (`FileWatcherService.cs:208`) mutates a plain `List<FileSystemWatcher>` with no lock while the rest of the type is `_reasonLock`-guarded; benign today (single-threaded `Watch()` caller) but an inconsistency a future caller could trip. ≤1 prod + ≤1 test, one regression shape.
- **(from row 1)** `filewatcher-class-xmldoc-truncated` — Low/XS docs: `FileWatcherService` class XML doc (lines ~22-26) ends mid-sentence ("…after the on-disk commit settles.") — a dropped clause; stale/incomplete doc comment.
- **(from row 2)** `ci-policy-cache-version-stale-cite` — Low/XS docs: `CI_POLICY.md:12` cites `actions/cache@v4` but `ci.yml:96` uses `actions/cache@v5` — stale version reference in the policy doc (pre-existing, unrelated to the vuln-gate change).
- **(from row 4)** `nuget-checker-timeout-test-bound-couple-to-httptimeout` — Low/S: `WaitForCompletionAsync`'s 30s hang-guard bound (`NuGetVersionCheckerTests.cs:122`) is a standalone literal; the `>> HttpTimeout(3s)` coupling is prose-only. Optionally make `HttpTimeout` internal + InternalsVisibleTo and derive the bound as a multiple, OR close won't-fix (comment as accepted control). Touches prod visibility → out of the test-only row's scope.

## Final step

After all rows land: confirm `ai_docs/backlog.md` rows closed (each via `/close-backlog-rows` during its ship), `updated_at` bumped, zero open PRs, clean tracking `main`. Append `## Retrospective` here.

## Retrospective

**Outcome: 5/5 shipped.** Zero open PRs; `main` clean + synced at `13420d2`; every feature branch (local + remote) pruned and verified gone.

### Shipped table

| Rank | Row id | PR | Squash-merge commit | CI validate | Reviews |
|------|--------|----|--------------------|-------------|---------|
| 1 | `filewatcher-waitforstale-clearstale-stranded-awaiter` | [#994](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/994) | `ef6aad6` | PASS 15m37s | spec PASS · cq PASS |
| 2 | `ci-vuln-gate-format-json-hardening` | [#995](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/995) | `08d8c46` | PASS 14m39s (new JSON gate ran live) | spec PASS · cq PASS (after in-PR stderr-isolation fix) |
| 3 | `publish-nuget-verify-package-types` | [#996](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/996) | `87c16ef` | PASS 13m29s | spec PASS · cq PASS (after in-PR disposal-nit fix) |
| 4 | `nuget-version-checker-timeout-test-wallclock-race` | [#997](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/997) | `0e989d0` | PASS 14m5s (de-flaked test ran live) | spec PASS · cq PASS |
| 5 | `dependabot-groups-batching` | [#998](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/998) | `13420d2` | PASS 11m40s | spec PASS · cq PASS |

Skipped: none of the selected 5. (Selection-stage skips: see §Selection rationale — 5 sweep-shaped L/plan-pointer rows + 2 BREAKING published-surface rows excluded.)

### Gate evidence (Directive #7)

- **Per-row local pre-check:** targeted `dotnet build` + `dotnet test --filter` (rows 1, 4) / `dotnet package list --format json` + synthetic-fail pwsh (row 2) / local `dotnet pack` + unzip pass+both-fail-path proofs (row 3) / `yaml.safe_load` schema parse (row 5). Outputs are in each implementer's returned report (transcript) + summarized in each PR body.
- **Authoritative gate:** each PR's `verify-release.ps1` `validate` job on the self-hosted runner (times above), polled green via `gh pr checks --watch` before merge. Full `verify-release.ps1` was NOT run locally (single-self-hosted-runner no-double-load).
- **Batch expected-reds:** row 4's known NuGetVersionChecker timeout flake was the only cross-row red risk (runs in `verify-release.ps1`, gates every PR). It did NOT trip on any sibling PR's CI this session, and row 4's fix (now landed) removes it structurally. No red required known-flake reclassification.

### Budget accounting (vs §Session budget ceilings, N=5)

- **Rows implemented:** 5 / 5 ceiling. ✅
- **Subagent spawns:** 18 / ≈19 ceiling (3N+4). Breakdown: prep 1 + selector 1 + [row1 3, row2 4 (one cq re-review after the in-PR stderr fix), row3 3, row4 3, row5 3]. ✅ (just under)
- **gh operations:** ~26 (5× pr-create + 5× watch + 5× merge + ~11 view/checks/ls-remote/orphan-sweep) / ≈40 ceiling (8N). ✅
- **Wall clock:** unbounded (no `budget-minutes`); dominated by 5 serial `validate` runs (~69 min CI) on the single self-hosted runner.

### Directive #3 call-outs filed (this docs PR)

4 spillover backlog rows added (Low), discovered during implementation, kept out of per-row PRs to preserve scope:
1. `filewatcher-watcherentry-watchers-unguarded-mutation` (S) — `FileWatcherService.cs:181/208` unguarded `_watchers` List mutation vs lock-protected rest of type.
2. `filewatcher-class-xmldoc-truncated` (XS docs) — `FileWatcherService.cs:~25` class XML doc clause ends with no verb.
3. `ci-policy-cache-version-stale-cite` (XS docs) — `CI_POLICY.md:12` cites `actions/cache@v4`; `ci.yml:96` uses `@v5`.
4. `nuget-checker-timeout-test-bound-couple-to-httptimeout` (S) — `NuGetVersionCheckerTests.cs:122` 30s bound is a literal; `>> HttpTimeout(3s)` coupling is prose-only (fixing requires prod-visibility widening, out of the test-only row's scope).

### Notes / handoffs

- **Operator-decision rows (NOT shipped — Directive #4):** `parameter-naming-canonicalization-migration` + `apply-composite-preview-destructive-misnomer` are size-safe but BREAKING published-surface changes needing an ADR + migration note + operator timing. Surfaced, not touched.
- **Plan-cleanup candidates** (`/reconcile-plans`): `20260618T143921Z_backlog-sweep` (all 8 merged), `20260619T180315Z_top-n-remediation` (prior run, complete), `audit-21-analyzers` (stale May-28 Draft). This plan dir joins the GC pool once its rows are confirmed shipped.
- **Upstream-refresh recommendation:** None — backlog still has implementation-ready rows.
