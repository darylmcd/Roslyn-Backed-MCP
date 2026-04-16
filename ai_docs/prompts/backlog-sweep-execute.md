# Backlog sweep — execute next pending initiative

<!-- purpose: Execute one initiative from the latest backlog-sweep plan, ship as PR. -->

You are picking up **one** initiative from a previously-produced backlog-sweep plan
(see `backlog-sweep-plan.md`). Run one PR, hand off, exit.

## Preconditions

- Work on a dedicated git branch (or worktree, per `ai_docs/workflow.md`).
- Ship one PR per initiative. The initiative may touch many files; "one concern" is
  the **shared root cause**, not the file count.
- Close all backlog rows cited by the initiative in the SAME PR (per
  `ai_docs/workflow.md` § Backlog closure).
- Add a CHANGELOG entry in the SAME PR (every initiative gets one — the project is
  publicly consumed, every shipped change is user-visible behavior change even when
  internal-correctness flavored).
- Do NOT auto-merge. Hand off at PR-open with validation green.

## Step 1 — Locate current plan

Find the most recent plan directory under `ai_docs/plans/*_backlog-sweep/` (sort by
the timestamp prefix descending). Read its `state.json`. If no plan directory exists,
report "no plan to execute — run `backlog-sweep-plan.md` first" and exit.

To pin a specific plan instead of the newest, the user may pass `plan-id=<dirname>`;
honor that override if supplied.

## Step 1b — Reconcile in-review initiatives, finalize if complete

Before picking new work, sync state.json + plan.md against reality. The previous
session(s) opened PRs and exited; some may have merged, some may have been closed
without merge.

For each initiative with `status == "in-review"`:

- `Bash: gh pr view <prUrl> --json state,mergedAt,mergeCommit` (or equivalent).
- **If `state == "MERGED"`:** update state.json (`status → "merged"`, `mergedAt → <ISO>`)
  AND update plan.md's Status row for this initiative to
  `merged (PR #<n>, <YYYY-MM-DD>)`. Commit both edits on `main`.
- **If `state == "CLOSED"` and not merged:** update state.json (`status → "deferred"`,
  `notes → "PR #<n> closed without merge — manual triage required"`) and plan.md's
  Status row to `deferred (PR #<n> closed; see notes)`. Surface this in your Step 10
  report so the human knows to either reopen + fix, or remove from the plan.
- **If `state == "OPEN"`:** leave as `in-review`. Skip.

**After reconciliation, check completion:**

- If every initiative in `state.json.initiatives` now has status `merged`, `obsolete`,
  or `deferred` (i.e. nothing `pending` and nothing `in-progress` and nothing
  `in-review`):
  1. Mark state.json with top-level `completed: true` and `completedAt: <ISO>`.
  2. Add a Refs entry to `ai_docs/backlog.md` pointing at the completed plan, matching
     the existing pattern (e.g. `ai_docs/plans/20260415T220000Z_top10-remediation-plan.md`
     in the current Refs table). Format:
     `| \`ai_docs/plans/{ts}_backlog-sweep/plan.md\` | Backlog sweep ({ts}). Shipped <N> initiatives across <M> PRs; closed <K> backlog rows (P2: <a>, P3: <b>, P4: <c>). |`
  3. Commit these two edits on `main`.
  4. Report "plan {dirname} fully shipped on {date} — run `backlog-sweep-plan.md` to
     start a new sweep" and exit.

Do NOT delete plan.md or state.json. They are the rationale record per the existing
`ai_docs/plans/` convention.

## Step 2 — Select next initiative

From `state.json.initiatives`, filter to `status == "pending"`, sort by `order`
ascending, take the first. If no pending initiative exists, report "all initiatives
complete" and exit.

If the selected initiative has `scheduleHint == "heroic-last"`: verify all other
non-heroic initiatives are `merged` or `obsolete` before proceeding. If not, skip
this one and pick the next pending non-heroic.

## Step 3 — Set up worktree

Create a git worktree at `.worktrees/{initiative.id}/` on branch
`remediation/{initiative.id}` from `main`. Update `state.json` (in main checkout, not
the worktree):

- `status` → `in-progress`
- `branch` → `remediation/{initiative.id}`
- `worktreePath` → `.worktrees/{initiative.id}`

**Also update `plan.md`'s Status row** for this initiative to
`in-progress (branch: remediation/{initiative.id}, worktree: .worktrees/{initiative.id})`.

Commit BOTH state.json and plan.md edits on `main` in a single commit so concurrent
sessions see this initiative is taken AND the human-readable plan stays in sync.

## Step 4 — Verify plan is still accurate

Re-read plan.md section for this initiative. Re-read the source files cited in
Diagnosis. If the behavior has changed since planning (another merge landed):

- **If the initiative is still relevant but the diagnosis shifted:** re-plan in-place
  by updating plan.md with the revised diagnosis/approach. Commit the plan update on
  the worktree branch BEFORE writing code, so the PR shows the re-plan transparently.
- **If the initiative is now obsolete:** set `status → "obsolete"` in state.json on
  main AND update plan.md's Status row to `obsolete (<one-line reason>)`. Commit
  both with a note explaining why, exit. Do NOT skip silently to the next
  initiative — surface the obsolescence so the next planning pass picks up cleaner.

## Step 5 — Implement

Follow the Approach field in plan.md. Rules:

- Use `Edit`/`Write` for code changes. Do NOT use Roslyn MCP `*_apply` on the Roslyn
  MCP codebase itself (bootstrap caveat in plan's `state.json.bootstrapCaveat`).
- Use `roslyn-mcp:review`/`:complexity`/`:dead-code` READ-ONLY to verify your changes
  don't regress adjacent code.
- After each meaningful edit, run:
  `Bash: dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`
  If red, fix before proceeding.
- Add or update tests per the Validation field. Tests MUST cover the specific
  regression (not just "the class still works"). One regression test per closed
  backlog row is the floor; more if the row covers multiple symptoms.

## Step 6 — Validate

Run the full CI-equivalent locally (per `CI_POLICY.md`):

- `Bash: ./eng/verify-ai-docs.ps1`
- `Bash: ./eng/verify-release.ps1 -Configuration Release`

If `verify-release.ps1` fails: fix before PR. Do NOT push broken work and "let CI
catch it" — CI minutes are a shared resource and your plan step said validation is
part of the initiative.

## Step 7 — Backlog and changelog sync (in same commit set)

Both edits land in this initiative's PR:

### 7a. `ai_docs/backlog.md`

- Remove the closed rows from the appropriate P-band table.
- For any row marked `obsolete` during Step 4: remove it too, with a note in the PR
  description explaining why.
- For any cross-referenced row that needs its `Refs:`/dependency section adjusted
  (per the plan's Backlog sync field): update inline.
- Bump the `updated_at:` timestamp at the top of `backlog.md`.

### 7b. `CHANGELOG.md` (REPO ROOT — not under `ai_docs/`)

- Confirm `## [Unreleased]` section exists at top. If not, add it above the most
  recent versioned section.
- Insert the initiative's pre-drafted CHANGELOG entry (from plan.md's
  "CHANGELOG entry (draft)" field) under the appropriate category subsection
  (`### Fixed`, `### Added`, `### Changed — BREAKING`, or `### Maintenance`).
  If the subsection doesn't exist yet, create it.
- Update the `### Maintenance` subsection's "Backlog: N rows closed" tally to include
  this initiative's closed rows under the correct P-band bullet (`P2 (n)`, `P3 (n)`,
  `P4 (n)`). Pattern: see existing release entries (e.g. `[Unreleased]` block in the
  current `CHANGELOG.md`) for tone, prefix-bolding, and row-id citation style.
- Update the test-count line in `### Maintenance` if your initiative added tests
  (line pattern: `Tests: <prev> → <new> (+N across <count> new regression test files).`).

## Step 8 — Open PR

Use the `ship` skill if available, otherwise `Bash: gh pr create ...`.

PR title: `{type}({scope}): {short description} ({initiative.id})`. Examples:
`fix(refactoring): create_file_apply no longer reserializes unrelated csprojs (create-file-apply-csproj-side-effect-all-projects)`.

PR body must include:

- **Closes:** explicit list of backlog row ids closed (machine-greppable).
- **Validation:** verify-release.ps1 outcome, test counts (before → after), any
  notable manual checks.
- **Performance:** before/after numbers if Step 4 of plan declared a perf review.
- **Spin-offs:** any new backlog rows added during verification (Step 3 of the plan).

## Step 9 — Update state

In the main checkout (not the worktree), update `state.json` for this initiative:

- `status` → `in-review`
- `prUrl` → `<url>`
- `notes` → any caveats for the human reviewer

**Also update `plan.md`'s Status row** for this initiative to `in-review (PR #<n>)`.

Commit BOTH state.json and plan.md edits on `main` in a single commit (separate
commit, outside the worktree). The PR-merge → `merged` status transition happens at
the next session's Step 1b reconciliation pass — not here.

## Step 10 — Report and exit

Output a one-paragraph summary:

- Initiative id
- Rows closed (count + list)
- Files touched (count)
- PR URL
- CHANGELOG section the entry landed in
- Anything unusual the reviewer should look at first
- Whether the next-in-queue initiative is runnable without human input, or blocked

Then **exit**. Do NOT start the next initiative in the same session — context pressure
from a single large initiative already consumes significant budget, and the next
initiative's state may change after this PR merges.
