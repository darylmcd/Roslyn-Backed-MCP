---
name: reconcile-backlog-vs-issues
installed_as: reconcile-backlog-vs-issues
description: "Audit every `gh #NNN` reference in `ai_docs/backlog.md` against live GitHub Issue state and emit a 5-state triage report. Use when: chasing drift between backlog rows and Issues after merges, manual issue closures, label changes, or contributor inactivity; before a `/backlog-sweep:plan` run to surface zombie rows or stale reservations the planner shouldn't pick. Read-only — does not edit `backlog.md` or close Issues; produces a structured recommendation list the maintainer applies via `/close-backlog-rows` or manual edits."
user-invocable: true
argument-hint: "[--stale-days N] (default 60) — threshold for the reserved-stale classification"
---

# Reconcile Backlog vs GitHub Issues

You audit `ai_docs/backlog.md` against live GitHub Issue state. The backlog references Issues two ways:

1. **Reserved rows** — `**Reserved — [gh #NNN](https://github.com/<owner>/<repo>/issues/NNN) (good first issue); skip in sweeps until contributor pickup.**` — these rows are parked for outside contributors; sweeps must skip them.
2. **Tracked-only rows** — `[gh #NNN](...)` somewhere in the `do` cell with no Reserved marker — the backlog row is the canonical work item, the Issue is just a public mirror.

When PRs merge, Issues close, contributors abandon, or labels drift, the two sides desynchronize. This skill walks every `[gh #NNN]` reference, queries `gh issue view N`, classifies the result, and emits a triage report. **It does not mutate `backlog.md` or any Issue** — the maintainer applies recommendations manually (or via `/close-backlog-rows` for the easy cases).

This skill is read-only. There is no destructive failure mode. The only side effects are network reads via `gh`.

## Server discovery

This skill reads `ai_docs/backlog.md` and shells out to `gh`. Roslyn MCP is unrelated — no workspace loading, no analyzer calls. If you think you need Roslyn MCP, you are in the wrong skill.

## Input

`$ARGUMENTS` accepts a single optional flag:

- `--stale-days N` (default `60`) — the `updatedAt` threshold past which a Reserved row is classified `reserved-stale` (no contributor activity on the Issue for N days).

If `$ARGUMENTS` is empty, use the defaults.

## Preconditions (HARD GATES — refuse if any fail)

1. **`ai_docs/backlog.md` exists** at the repo root. If missing, refuse: `"No ai_docs/backlog.md at repo root. Are you in the right repository?"`.
2. **`gh` CLI is on PATH** (`gh --version` exits 0). If not, refuse: `"gh CLI not available — cannot query Issue state."`.
3. **`gh auth status` succeeds** — the CLI is authenticated against `github.com`. If not, refuse with the `gh auth login` hint.

## Workflow

### Step 1 — Scan backlog.md for `[gh #NNN]` references

Read `ai_docs/backlog.md`. For each non-header row in any P-band table (High / Medium / Low / Reference), extract:

- The row's `id` cell (between the first pair of backticks).
- Every `[gh #NNN]` reference inside the row's `do` cell. A single row CAN reference multiple Issues (rare — usually one).
- Whether the row text contains the literal `**Reserved` marker (case-sensitive, with the two leading asterisks).
- Whether the row text contains the literal `(good first issue)` parenthetical.

Produce an in-memory list of `(rowId, issueNumber, isReserved, hasGoodFirstIssueLabel)` tuples. De-duplicate identical tuples (defensive — should not happen).

If the list is empty, report `"No [gh #NNN] references found in ai_docs/backlog.md — nothing to reconcile."` and exit.

### Step 2 — Query GitHub for each unique Issue number

Collect the unique set of Issue numbers from Step 1. For each, run:

```bash
gh issue view <N> --json number,state,closed,closedAt,updatedAt,labels,title
```

**Rate-limit discipline.** `gh` does not auto-batch single-issue lookups. For ≤30 unique Issues, run sequentially. For >30, page in chunks of 10 with a brief pause between chunks to stay under the secondary-rate-limit threshold (~900 points/min for unauthenticated GraphQL; the REST `gh issue view` path is more generous but still bounded).

On transient failure (HTTP 5xx, network blip), retry once with a 2-second delay. On persistent failure, record `(issueNumber, error)` in a `failed-lookups` list and continue with the rest. Do NOT abort the whole report on a single failed lookup.

Parse each successful JSON response into a map: `issueNumber → { state, closed, closedAt, updatedAt, labels[], title }`. Note: `state` is `"OPEN"` or `"CLOSED"`; `labels` is an array of `{ name, color, ... }` objects.

### Step 3 — Classify each (rowId, issueNumber) pair

For each tuple from Step 1, look up the Issue state from Step 2 and assign exactly one classification:

| # | Classification | Condition | Recommendation |
|---|---|---|---|
| 1 | `issue-closed-row-open` | `state == CLOSED` AND `isReserved == false` | Run `/close-backlog-rows <rowId>` — the Issue closed (PR merged or manual close), the row should follow. |
| 2 | `issue-closed-row-reserved` | `state == CLOSED` AND `isReserved == true` | Run `/close-backlog-rows <rowId>` — the Reserved marker has nothing left to reserve; this is a zombie row. |
| 3 | `reserved-stale` | `state == OPEN` AND `isReserved == true` AND `updatedAt` older than `--stale-days` | Maintainer decision: reclaim (remove Reserved marker so sweep can plan against it) OR leave parked. The Issue had no contributor activity in N days. |
| 4 | `label-drift` | `state == OPEN` AND `hasGoodFirstIssueLabel == true` AND `labels[].name` does NOT contain `good first issue` | Refresh the row's `(good first issue)` parenthetical — the Issue's label was removed and the row text now lies. |
| 5 | `issue-reopened-row-missing` | The Issue is `OPEN` but its `closedAt` is non-null (reopened) AND no row currently references this Issue number — discovered by inverting the Step 1 set against a `gh issue list --state open --search "is:issue label:tracked-only"` query. | Manual row recreation via `/backlog-intake`. |

If none of the above match, classify as `ok` (no action). Most references will be `ok` — that is the healthy state.

**State 5 caveat.** The inverse search (Issues that exist but have no backlog row) requires a second `gh issue list` call against a label filter. If your repo does not use a `tracked-only` (or equivalent) label convention, this state is undetectable and should be **omitted** from the report rather than reported as zero — emit a one-line note that state 5 was skipped for lack of a label convention. The maintainer can hand-audit if desired.

### Step 4 — Emit the triage report

Print a structured report to stdout. Group by classification, in the order above. Within each group, sort by `rowId` ascending. Format:

```
Backlog ↔ GH Issues reconciliation — {YYYY-MM-DD HH:MM UTC}
  Backlog file: ai_docs/backlog.md ({total-row-count} rows, {total-gh-refs} [gh #NNN] references)
  Stale threshold: {N} days

== issue-closed-row-open ({k}) — run /close-backlog-rows ==
  {row-id-1}              gh #{n} (closed {YYYY-MM-DD}, "{issue-title-truncated}")
  {row-id-2}              gh #{n} (closed {YYYY-MM-DD}, "{issue-title-truncated}")

== issue-closed-row-reserved ({k}) — run /close-backlog-rows (zombie) ==
  {row-id}                gh #{n} (closed {YYYY-MM-DD})

== reserved-stale ({k}) — maintainer decision ==
  {row-id}                gh #{n} (updated {YYYY-MM-DD}, {days} days idle)

== label-drift ({k}) — refresh row text ==
  {row-id}                gh #{n} (row claims "good first issue", labels: {actual-labels})

== issue-reopened-row-missing ({k}) — manual /backlog-intake ==
  gh #{n}                 "{issue-title}" (reopened {YYYY-MM-DD})
  [or "Skipped — no tracked-only label convention in this repo."]

== ok ({k}) ==
  ({k}) references match expected state — no action.

== failed-lookups ({k}) ==
  gh #{n}                 {error-message}
  ({k} references could not be classified — retry the skill or check `gh auth status`.)

Summary: {issue-closed-row-open} + {issue-closed-row-reserved} closable, {reserved-stale} stale, {label-drift} drifted, {issue-reopened-row-missing} missing, {ok} ok, {failed-lookups} failed.
```

Truncate Issue titles to 60 chars. Use ISO short dates (`YYYY-MM-DD`) throughout.

### Step 5 — Suggest the next command

If `(issue-closed-row-open + issue-closed-row-reserved) > 0`, emit a single ready-to-run `/close-backlog-rows` invocation listing every closable row id:

```
Suggested next step:
  /close-backlog-rows {row-id-1},{row-id-2},{row-id-3}
```

The maintainer copy-pastes that line. Do NOT auto-run `/close-backlog-rows` — this skill is read-only.

If `reserved-stale > 0`, emit a one-line nudge: `"{k} reserved rows have been idle ≥ {N} days. Consider reclaiming via manual backlog.md edit (remove the **Reserved — [gh #N] (good first issue); skip in sweeps...** prefix from the do cell)."`.

If `label-drift > 0`, emit: `"{k} rows claim 'good first issue' but the Issue no longer carries the label. Update each row's parenthetical or remove it."`.

If the report is entirely `ok` + `failed-lookups == 0`, emit: `"Backlog ↔ GH Issues are reconciled — no action."`.

## Refusal cases (explicit)

- **`ai_docs/backlog.md` missing** → refuse per Precondition 1.
- **`gh` not on PATH / not authenticated** → refuse per Preconditions 2-3.
- **`gh` rate-limit on >90% of lookups** → stop after the first 10 consecutive failures, report partial results with `failed-lookups` populated, and suggest re-running after the rate window resets.

## Distinct from related skills

- **`/close-backlog-rows`**: deletes rows by id. This skill identifies WHICH rows are closable; `/close-backlog-rows` does the deletion. Two-step pipeline.
- **`/reconcile-backlog-sweep-plan`**: reconciles a backlog-sweep plan's `state.json` + `plan.md` against PR state. Different file set, different mutation target. Both skills run "between waves" but address different drift sources (PR↔plan vs Issue↔backlog).
- **`/backlog-intake`**: ingests deep-review artifacts into new backlog rows. This skill can flag `issue-reopened-row-missing` as an input signal for `/backlog-intake`, but never invokes it directly.

## Example output

Input: `/reconcile-backlog-vs-issues --stale-days 90`

```
Backlog ↔ GH Issues reconciliation — 2026-05-11 19:30 UTC
  Backlog file: ai_docs/backlog.md (47 rows, 19 [gh #NNN] references)
  Stale threshold: 90 days

== issue-closed-row-open (2) — run /close-backlog-rows ==
  test-related-column-required-schema-mismatch    gh #618 (closed 2026-05-08, "test_related schema marks column optional...")
  semantic-search-grep-pattern-broken             gh #627 (closed 2026-05-09, "semantic_search and semantic_grep both...")

== issue-closed-row-reserved (1) — run /close-backlog-rows (zombie) ==
  goto-type-definition-builtins-invalidoperation  gh #607 (closed 2026-05-07)

== reserved-stale (1) — maintainer decision ==
  add-project-reference-self-reference-not-rejected   gh #608 (updated 2026-02-01, 99 days idle)

== label-drift (0) ==

== issue-reopened-row-missing — Skipped — no tracked-only label convention in this repo.

== ok (15) ==
  (15) references match expected state — no action.

== failed-lookups (0) ==

Summary: 3 closable, 1 stale, 0 drifted, 0 missing, 15 ok, 0 failed.

Suggested next step:
  /close-backlog-rows test-related-column-required-schema-mismatch,semantic-search-grep-pattern-broken,goto-type-definition-builtins-invalidoperation

1 reserved row has been idle ≥ 90 days. Consider reclaiming via manual backlog.md edit (remove the **Reserved — [gh #N] (good first issue); skip in sweeps...** prefix from the do cell).
```

## Why this exists

Four distinct drift gaps existed before this skill, surfaced in the 2026-05-11 adversarial audit:

1. **PR-close ↔ row-close reverse sync** — a PR with `Fixes #N` closes Issue N, but the corresponding backlog row remains until the next manual sweep notices.
2. **Manual Issue closure not propagated** — a maintainer closes an Issue from the web UI; the row stays.
3. **Reserved + closed-Issue zombie rows** — Reserved markers point at Issues that have already been merged by a contributor.
4. **Label drift** — `good first issue` label removed (e.g. contributor claimed it then abandoned) but row text still advertises it.

This skill catches all four in one pass, plus the 5th state (reopened-but-not-tracked) where the label convention permits.
