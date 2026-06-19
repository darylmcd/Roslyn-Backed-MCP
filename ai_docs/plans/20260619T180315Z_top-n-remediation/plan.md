# Top-N Remediation — 20260619T180315Z

**Invocation:** `/top-n-remediation count=10` · **N=10** · autonomous (no gate, no budget-minutes, no resume).
**Land mode:** default immediate-land per row (ship-loop steps 1–6 fully per row; clean `main` between rows). Chosen over batch-land: single self-hosted CI runner + possible up-to-date-branch protection ⇒ batch-land would churn re-CI against a moving `main`.
**Preflight:** `main` clean (0/0 vs origin); `gh` merge rights OK; **0 open PRs** (orphaned-PR sweep empty); active plan `20260618T143921Z_backlog-sweep` all-merged ⇒ `OWNED_BY_ACTIVE_PLAN` empty. Both exclusion sets empty.
**Validation policy:** per-row local validation is targeted (changed-path build/test, format) — full `verify-release.ps1` is NOT run locally (self-hosted runner runs it on each PR). CI green confirmed via `watch-pr`/`gh pr checks` before merge.

## Selection

| id | rank | reasons | est. file touches |
|---|---|---|---|
| `ci-flaky-fswatcher-staleness-test` | 1 | High pri; release-gating flake (fails NuGet+registry publish on ubuntu) | 1 test |
| `ci-vuln-audit-gating` | 2 | Medium; security gate hole — `dotnet package list --vulnerable` exits 0 on CVEs | 1 workflow yaml |
| `repo-dependabot-config` | 3 | Medium; net-new `dependabot.yml` + bump stale `actions/*@v4` (Node20 deprecation) | 1 new yaml + workflow bumps |
| `nuget-mcpserver-gallery-packaging` | 4 | Medium; MCP-gallery listing — `<PackageType>McpServer</PackageType>` + `.mcp/server.json`; pack-test required | 1 csproj + 1 json (+ maybe workflow) |
| `registry-readiness-linter-warn-relax` | 5 | Low; false-positive warn relax — accept owner-namespace name mismatch | 1 ps1 |
| `aggregate-scorecard-stale-search-path` | 6 | Low; drop removed `ai_docs/audit-reports/` probe + reconcile SKILL docs | 1 ps1 + 2 SKILL md |
| `revert-last-apply-single-slot-doc-warning` | 7 | Low; tool-description text — state single-slot LIFO loudly, x-ref `revert_apply_by_sequence` | 1 cs (tool attr) |
| `legacy-bug-id-msbuild-eval-comments` | 8 | Low; strip `BUG-008` ids from 2 cited file:line comments | 2 cs (comment-only) |
| `symboltools-twin-null-guard-comment-exemplars` | 9 | Low; unify two divergent null-guard comment citations | 1 cs (comment-only) |
| `workspace-validation-kill-test-reflection-seam` | 10 | Low; swap reflection for injected `killProcessTree` seam in one test | 1 test |

## Per-row state

- `ci-flaky-fswatcher-staleness-test`: landed (PR #978, squash `a7dbe4d`; CI validate green 11m35s; spec+quality pass; filed 2 advisory rows: `filewatcher-waitforstale-clearstale-stranded-awaiter` M, `externaledit-test-rearm-marker-file-type-neutral` L)
- `ci-vuln-audit-gating`: landed (PR #979, squash `90979ac`; CI validate green 10m9s; code-quality cycle-1 caught HIGH fail-open exit-code masking, fixed + re-passed; filed advisory `ci-vuln-gate-format-json-hardening` M)
- `repo-dependabot-config`: landed (PR #980, squash `6ab9215`; CI validate green 10m18s on bumped actions; spec+quality pass; filed advisory `dependabot-groups-batching` L)
- `nuget-mcpserver-gallery-packaging`: landed (PR #986, squash `64e0eb8`; local pack-validated both package types + embedded manifest; orchestrator forward-slashed path + re-validated; CI flaked once on unrelated `NuGetVersionCheckerTests` wall-clock race, re-ran green 10m13s; spec+quality pass; filed advisory `publish-nuget-verify-package-types` M + flake row to file in row-5 sync)
- `registry-readiness-linter-warn-relax`: landed (PR #987, squash `6cff5d3`; CI validate green 10m23s no flake; script 27/0/0; spec+quality pass; filed advisories `nuget-version-checker-timeout-test-wallclock-race` M + `registry-readiness-url-regex-canonical-only` L)
- `aggregate-scorecard-stale-search-path`: landed (PR #988, squash `b70c8d9`; CI validate green 11m2s; spec cycle-1 caught missing AC3 test, fixed + re-passed; quality pass; filed advisory `aggregate-scorecard-includeself-double-count` L)
- `revert-last-apply-single-slot-doc-warning`: landed (PR #989, squash `753473e`; CI validate green 11m19s; spec+quality pass; 1 low advisory (description length) NOT filed — conflicts with row's "state loudly" intent, surfaced in report)
- `legacy-bug-id-msbuild-eval-comments`: landed (PR #990, squash `05ab945`; CI validate green 10m39s; spec+quality pass; comment-only, grep-clean)
- `symboltools-twin-null-guard-comment-exemplars`: landed (PR #991, squash `87fc2f1`; CI validate green 10m30s; spec+quality pass; citation verified factually correct)
- `workspace-validation-kill-test-reflection-seam`: landed (PR #992, squash `8bc5e3c`; CI validate green 10m53s; spec+quality pass; removes a reflection-into-private coupling smell)

States advance in place: `selected → implemented → reviewed → pr-open → landed`; terminal `skipped:<why>` / `ship-failed:<why>`.

## Final step

- `backlog: sync ai_docs/backlog.md` — close each landed row via `/close-backlog-rows` (atomic row + `items/<id>.md` delete) during each row's ship-loop step 2.

## Retrospective

**Outcome: 10/10 selected rows shipped & merged. Zero open PRs, clean tracking `main` (0/0 vs origin). No skips, no ship-failures.**

### Shipped (per-row, with gate evidence)

| rank | row id | PR | squash merge | CI gate evidence (`validate` job) | review notes |
|---|---|---|---|---|---|
| 1 | ci-flaky-fswatcher-staleness-test | #978 | `a7dbe4d` | green 11m35s | spec+quality pass; event-driven `WaitForStaleAsync` seam |
| 2 | ci-vuln-audit-gating | #979 | `90979ac` | green 10m9s | quality cycle-1 caught **HIGH** fail-open exit-code masking → fixed → re-passed |
| 3 | repo-dependabot-config | #980 | `6ab9215` | green 10m18s | action majors ground-verified vs GitHub API (checkout@v7/cache@v5/upload-artifact@v7) |
| 4 | nuget-mcpserver-gallery-packaging | #986 | `64e0eb8` | green 10m13s (1 flake re-run) | local `dotnet pack` validated both package types + embedded manifest; orchestrator forward-slashed path for ubuntu + re-validated |
| 5 | registry-readiness-linter-warn-relax | #987 | `6cff5d3` | green 10m23s | script `27 pass / 0 fail / 0 warn` |
| 6 | aggregate-scorecard-stale-search-path | #988 | `b70c8d9` | green 11m2s | spec cycle-1 caught missing AC3 regression test (implementer wrongly claimed no harness existed) → fixed → re-passed; ground-checked #937 to resolve the row's muddled framing |
| 7 | revert-last-apply-single-slot-doc-warning | #989 | `753473e` | green 11m19s | spec+quality pass |
| 8 | legacy-bug-id-msbuild-eval-comments | #990 | `05ab945` | green 10m39s | comment-only, src grep-clean |
| 9 | symboltools-twin-null-guard-comment-exemplars | #991 | `87fc2f1` | green 10m30s | quality verified citation factually correct |
| 10 | workspace-validation-kill-test-reflection-seam | #992 | `8bc5e3c` | green 10m53s | removes reflection-into-private coupling smell |

Gate evidence lives on the PRs (each `validate` job log linked from `gh pr checks <n>`). All merges squash; each row branched from a clean `main` and `main` was re-confirmed clean (`git status` + `git ls-remote` empty) before the next branch was cut. One CI failure total (PR #986) was an **unrelated** `NuGetVersionCheckerTests` wall-clock flake (1560/1561 passed; re-ran green with zero code change) — classified, re-run, and filed as a stabilization row, not papered over.

### Directive #3 follow-ons filed (8 new rows)

| id | pri | source |
|---|---|---|
| filewatcher-waitforstale-clearstale-stranded-awaiter | Medium | row 1 quality review (ClearStale strands a parked awaiter) |
| externaledit-test-rearm-marker-file-type-neutral | Low | row 1 quality review |
| ci-vuln-gate-format-json-hardening | Medium | row 2 quality review (prefer `--format json` over English-substring match) |
| dependabot-groups-batching | Low | row 3 quality review |
| publish-nuget-verify-package-types | Medium | row 4 quality review (verify step doesn't assert package types) |
| nuget-version-checker-timeout-test-wallclock-race | Medium | flake surfaced during row-4 CI (recurrence of an incompletely-fixed flake) |
| registry-readiness-url-regex-canonical-only | Low | row 5 quality review |
| aggregate-scorecard-includeself-double-count | Low | row 6 implementer finding (-IncludeSelf skews quorum) |

**Not filed (reasoned):** row 7's low "tighten the description length" advisory — the +309 chars *is* the loud footgun warning the row explicitly required ("state loudly"), so a tightening row would pull against the row's own acceptance; surfaced here instead. Row 9's "self-reference in sibling list" — reviewer judged it reads as convention exemplars, not a defect.

Net backlog: −10 closed, +8 filed = **−2 actionable rows** (35 open after the run; the 3 Defer rows untouched).

### Budget accounting (vs §Session-budget ceilings, N=10)

| resource | used | ceiling (≈3×N+4 / 8×N) | note |
|---|---|---|---|
| Rows implemented | 10 | 10 | full set |
| Subagent spawns | ~36 (2 selection + 12 implementer incl. 2 fix re-dispatches + 22 reviewers incl. 3 re-review cycles) | ~34 | **slightly over** — entirely from legitimate review-driven rework (1 HIGH-severity fix, 1 missing-AC3-test fix); not runaway. Each re-cycle improved correctness. |
| gh operations | ~50 (push/create/checks/merge/ls-remote per row + 1 rerun) | ~80 | within |
| Wall clock | unbounded (no `budget-minutes`) | — | dominated by serial CI waits (~10–11 min/PR on the single self-hosted runner) |

Confirmed 1M+high session: drained all 10 rows without pausing for a context figure (per `~/.claude/CLAUDE.md` exception); no compaction/truncation pressure signal observed.

### What went well / what to watch

- **Immediate-land per row** was the right call for a single self-hosted runner — every PR's CI ran once with no rebase churn; main moved linearly.
- **Cold two-stage review caught two real defects** the implementer missed (row 2 fail-open security gate; row 6 doc-comment-not-a-test) — the adversarial pass earned its keep.
- **Directive #5 re-derivation mattered on row 6:** the row's own framing conflicted with the SKILL docs; only reading PR #937's actual diff settled which path was canonical (the docs were stale, the row was right).
- **Recurring flake** (`NuGetVersionCheckerTests` wall-clock race) is the same class as row 1's fix and a recurrence of a prior "fixed" row — filed for a real stabilization, not a known-flakes registry entry.
- **Side effect:** row 3 activated Dependabot, which has begun opening its own update PRs (`dependabot/nuget/*`) — these are bot PRs for the operator to review, independent of this run.
