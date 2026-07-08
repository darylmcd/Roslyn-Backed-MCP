# Top-N Remediation Plan — 20260621T135113Z

**Invocation:** `/top-n-remediation` (no args) → N=5, autonomous (no gate=ask), no budget-minutes, no resume, no contextPct.
**Backlog snapshot:** `ai_docs/backlog.md` updated_at `2026-06-21T02:45:32Z` (33 open rows: 3 Defer, 27 Low, 3 Medium).
**Plan-collision:** none — all initiatives across the 3 active `*_backlog-sweep` plans are terminal (merged/obsolete/deferred). `OWNED_BY_ACTIVE_PLAN` = ∅.
**Orphaned-PR sweep:** ∅ row-id PRs. The 4 open PRs (#1000–#1003) are all dependabot nuget bumps — not backlog rows, excluded from this run.
**Validation policy:** self-hosted runner ACTIVE → no local `verify-release.ps1` / full `dotnet test`. CI on PR (`watch-pr` to green) is the authoritative gate; local = lightweight build/compile/diff. All 5 rows touch `.ps1`/`.cs` (not docs-only) → each PR runs full `verify-release` on CI.
**Execution shape:** immediate-land per row (default), strict serial implementers. Rows 4→5 sequenced (shared `FileWatcherService.cs`) so the next branch is cut from a main already carrying the prior change → no overlap conflict.

## Selection

| id | rank | reasons | estimated file touches |
|----|------|---------|------------------------|
| `registry-readiness-url-regex-canonical-only` | 1 | Low/S, shovel-ready, 0 signals; resolution = one-line clarifying comment + close won't-fix | 1 (`eng/verify-registry-readiness.ps1`) |
| `aggregate-scorecard-includeself-double-count` | 2 | Low/S, shovel-ready, 0 signals; latent quorum-math bug, real correctness fix + regression test | 2 (`eng/aggregate-promotion-scorecards.ps1`, `tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs`) |
| `externaledit-test-rearm-marker-file-type-neutral` | 3 | Low/S, shovel-ready, 0 signals; file-type-neutral test marker | 1 (`tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`) |
| `filewatcher-watcherentry-watchers-unguarded-mutation` | 4 | Low/S, shovel-ready, 0 signals; document single-threaded invariant (smallest fix) | 1 (`src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`) |
| `filewatcher-class-xmldoc-truncated` | 5 | Low/S, shovel-ready, 0 signals; complete truncated class XML-doc clause | 1 (`src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`) |

**File-overlap matrix (expected-reds):** only rows 4 & 5 share a file (`FileWatcherService.cs`); sequenced immediate-land ⇒ no batch-sibling reds. All other rows disjoint.

## Per-row state

- `registry-readiness-url-regex-canonical-only`: landed (PR #1018, squash 942417e)
- `aggregate-scorecard-includeself-double-count`: landed (PR #1019, squash 01b0cda) — CI infra flake (runner comms-drop) cleared by job re-run, not a code failure
- `externaledit-test-rearm-marker-file-type-neutral`: landed (PR #1020, squash 13874b2)
- `filewatcher-watcherentry-watchers-unguarded-mutation`: landed (PR #1021, squash a73fa13) — comment tightened post-review for invariant accuracy
- `filewatcher-class-xmldoc-truncated`: landed (PR #1022, squash 6fa6f82) — also filed follow-up row `aggregate-scorecard-test-runner-dedup` (row-2 cq spillover)

## Final step

- backlog: sync `ai_docs/backlog.md` — each shipped row closed via `/close-backlog-rows` (deletes row + `items/<id>.md`) during its ship leg; changelog fragment per row under `changelog.d/`.

## Retrospective

**Outcome:** 5/5 selected rows shipped + 1 follow-up row filed. Zero open row-id PRs; `main` clean at `6fa6f82`, tracking `origin/main`. Backlog: 33 → 28 (5 closed) → 29 (1 spillover filed).

### Shipped / skipped table

| # | Row | Result | PR | Squash commit | Gate evidence |
|---|-----|--------|----|---------------|---------------|
| 1 | `registry-readiness-url-regex-canonical-only` | shipped | [#1018](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1018) | `942417e` | CI `validate` green (job 82576585931) |
| 2 | `aggregate-scorecard-includeself-double-count` | shipped | [#1019](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1019) | `01b0cda` | CI `validate` green on re-run (82604767671); first run red on transient runner comms-drop |
| 3 | `externaledit-test-rearm-marker-file-type-neutral` | shipped | [#1020](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1020) | `13874b2` | CI `validate` green (82605817912) |
| 4 | `filewatcher-watcherentry-watchers-unguarded-mutation` | shipped | [#1021](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1021) | `a73fa13` | CI `validate` green (82606933524) |
| 5 | `filewatcher-class-xmldoc-truncated` | shipped | [#1022](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1022) | `6fa6f82` | CI `validate` green (82608211064) |

**Validation command (gate of record):** `verify-release.ps1 -Configuration Release` (full restore/build/test/publish) + `verify-ai-docs.ps1` + `verify-skills-are-generic.ps1` + NuGet vuln audit + CodeQL — all run on the PR via the self-hosted runner. No local full-suite runs (runner active). Per-row local checks: PowerShell parse-check (rows 1–2), `dotnet build` (rows 3–5); row-2 implementer additionally ran the filtered `AggregatePromotionScorecardsScriptTests` class (red→green, 8/8) before push.

**Expected-reds:** only rows 4 & 5 shared a file (`FileWatcherService.cs`); sequenced immediate-land ⇒ no batch-sibling reds materialized. No known-flakes hit. Row 2's first-run red was an infra comms-drop (every real step ✓), cleared by `gh run rerun --failed` — reclassified `transient-infra`, not `introduced-by-this-row`.

### Budget accounting

| Resource | Used | Ceiling (N=5) | Within? |
|----------|------|---------------|---------|
| Rows implemented | 5 | 5 | ✅ |
| Subagent spawns | 17 (prep 1 + selector 1 + implementers 5 + spec-reviewers 5 + cq-reviewers 5) | ≈19 (3×N+4) | ✅ |
| gh operations | ~32 (create 5, checks/watch ~12, view ~6, merge 5, rerun 1, ls-remote 5) | ≈40 (8×N) | ✅ |
| Wall clock | ~70 min (5 CI cycles + 1 rerun) | unbounded (no budget-minutes) | ✅ |
| Re-dispatches | 0 (all subagents well-formed first try) | — | — |

### Directive #3 call-outs filed
- `aggregate-scorecard-test-runner-dedup` (Low) — `RunAggregatorWithIncludeSelf` duplicates `RunAggregator`'s process-launch body (row-2 cq medium finding, advisory). Filed in PR #1022.
- Reviewers found no other bad code across the 5 diffs. Row-5 implementer noted a mild doc-duplication at `MarkStaleIfRelevant` (intentional micro-context comment) — judged not row-worthy.

### Process notes
- Row 4's invariant comment was tightened post-spec-review (the reviewer flagged "before the entry's watchers begin raising events" as imprecise — watchers can fire mid-construction; they just never touch `_watchers`). Corrected to be exact; code-quality review then confirmed accuracy.
- **Orchestrator error (self-caught):** an add-flag probe ran `git checkout -- ai_docs/backlog.md`, reverting Row 5's uncommitted close and re-introducing the row (orphan-row vs deleted detail). Detected via `backlog-lint`, re-closed cleanly. Lesson: never `git checkout --` a file holding intended uncommitted changes.
- Plan dir persisted on-disk (untracked) as the durable session artifact; visible to `/reconcile-plans` GC and `/backlog-sweep:status`. Not committed (ephemeral; `/reconcile-plans` will GC it on completion).

### Cold review (self-reflection, HIGH blast radius)
- Independent cold reviewer over the 5 squash commits: verdict **GO** — all 5 correct, in-scope, acceptance met; backlog-lint 0 ERROR; commit-2 regression test confirmed genuinely red pre-fix (not tautological) and cannot over-exclude the hub; commit-3 marker never empty in-loop; commits 4 & 5 comments verified accurate against code.
- Cold reviewer surfaced one PRE-EXISTING Directive #3 finding (not from this run, #267 Apr 2026): stale `MarkStaleIfRelevant` comment (`FileWatcherService.cs:154-155`) claims external-edit precedence the code (unconditional last-writer-wins) doesn't implement. Self-verified, then filed as row `filewatcher-markstaleifrelevant-stale-precedence-comment` (Low/S) via PR #1023 (`8e1810d`).
