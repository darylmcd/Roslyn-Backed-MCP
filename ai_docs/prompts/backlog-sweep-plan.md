# Backlog sweep — planning pass

<!-- purpose: Initiative-level plan that groups related backlog rows into shippable PRs. -->

You are a senior engineer working in the Roslyn-Backed-MCP repo. Your job is to read
`ai_docs/backlog.md` and produce an **initiative-level** implementation plan that groups
related rows into shippable PRs. Companion: `backlog-sweep-execute.md` consumes the
`state.json` you produce here.

## Project constraints

- **No backward compatibility requirements.** Breaking changes to public APIs, schemas,
  response shapes, parameter names, and internal contracts are acceptable.
- **Large refactors are on the table.** If the right fix is to restructure a service,
  rename symbols across the codebase, or delete and rebuild a component — propose it.
- **Priorities, strict order:** (1) code correctness, (2) performance, (3) SRP.
  Correctness always wins. When correctness risk ties, use blast radius, then SRP
  improvement, then cost.
- **Bootstrap caveat.** The codebase being planned against is the Roslyn MCP server
  itself. Execution sessions will use `Edit` + `Bash: dotnet build` + `compile_check`,
  NOT `*_apply`/`*_preview` self-application. See backlog row
  `self-edit-bootstrap-mode-mcp-development` and the qualitative session notes at the
  bottom of `backlog.md` for why. Do not plan around tools you cannot use on yourself.

## Step 1 — Candidate selection (row-level)

Read `ai_docs/backlog.md` (open/unfinished work only). Shortlist rows by:

1. **Correctness risk** — items where current behavior silently corrupts user data or
   produces wrong results rank highest (P2 first, then P3 correctness-flavored rows).
2. **Blast radius** — how many tools, components, or consumer workflows are affected.
3. **Feasibility** — can be completed in a single focused PR without unresolved external
   dependencies. Skip rows whose `deps` field references unfinished work.

Do NOT cap at an item count. Go deep enough to cover every P2 + all P3 rows + the
highest-impact P4 rows. The item count is not the output — the **initiative count** is.

## Step 2 — Consolidation discovery

For every shortlisted row, search the backlog (and recent audit reports under
`ai_docs/audit-reports/` if useful) for cross-references:

- Explicit phrases: "same response-size story as", "same shape as", "consolidates",
  "see also", "Recommendation #N", "shared root cause".
- Shared service/file citations in the `Refs:` section of each row.
- Shared symptom keywords ("Formatter.FormatAsync missing", "output size exceeds cap",
  "`-32603` generic error", "preview-token invalidated").

**Merge rows with shared root cause into a single initiative.** An initiative closes
1-N backlog rows. Record the multiplier (rows-closed count) — it's a tie-breaker later.

## Step 3 — Independent verification (per initiative)

For each initiative, do NOT trust the backlog text as ground truth. It may be stale
or wrong. Verify:

- Read the actual source files cited. Confirm the behavior still exists.
- Use `roslyn-mcp:review`, `roslyn-mcp:complexity`, `roslyn-mcp:dead-code` READ-ONLY
  skills against the touched code to surface adjacent issues (do not apply fixes —
  bootstrap caveat). Cite their output in the diagnosis.
- If the behavior no longer reproduces: drop the row, mark it `obsolete: <id>` in
  the initiative's Backlog sync field, and note the swap. Do NOT silently replace
  with a lower-priority candidate.
- Identify root cause in code, not just symptoms (file:line references).

**Adjacent perf smell check** on the files you'll already touch: (a) repeated full-
collection scans where scoped/cached access exists; (b) synchronous enumeration of
large data sets; (c) redundant expensive work inside loops; (d) operations that pull
more data than the call needs. Record findings. If a perf fix requires touching files
outside the correctness work, **spin it off as a new backlog row** — do not bundle.

## Step 4 — Initiative plan (per initiative)

Produce this table for each initiative:

| Field | Content |
|-------|---------|
| **Initiative id** | Kebab-case, grep-friendly (e.g. `output-size-summary-mode-family`) |
| **Status** | One of: `pending`, `in-progress`, `in-review`, `merged`, `obsolete`, `deferred`. Initial value: `pending`. The executor updates this row at every state transition so plan.md remains a self-contained recovery source if state.json is lost or corrupted. Mirrors `state.json.initiatives[].status`. Format examples: `pending` · `in-progress (branch: remediation/foo, worktree: .worktrees/foo)` · `in-review (PR #163)` · `merged (PR #163, 2026-04-17)` · `obsolete (no longer reproducible — see notes)`. |
| **Backlog rows closed** | List of row ids (1 or more) |
| **Diagnosis** | What you found in the code (file:line). How it differs from or confirms the backlog description. Include `roslyn-mcp:review` output citations. |
| **Approach** | Concrete implementation strategy. Name files to create/modify, functions to change, patterns to follow. |
| **Scope** | Files touched, files created, files deleted. |
| **Risks** | What could go wrong; what adjacent behavior to verify. |
| **Validation** | How to confirm the fix: specific test cases (new or existing), build checks (`dotnet build -p:TreatWarningsAsErrors=true`), manual reproduction steps. |
| **Performance review** | ONLY if the fix touches a hot path (I/O, loops, response serialization). Otherwise: "N/A — correctness fix, no hot-path changes." If included: baseline using existing mechanism (test timings, `verify-release.ps1` output, MCP response-size from audit reports, structured logs) and expected post-fix measurement. No intuition — cite numbers. |
| **CHANGELOG category** | Which `Keep a Changelog` category this slots into: `Fixed`, `Added`, `Changed — BREAKING`, or `Maintenance`. Most correctness fixes are `Fixed`. New tools/parameters are `Added`. Removed/renamed APIs are `Changed — BREAKING`. |
| **CHANGELOG entry (draft)** | Pre-drafted entry text in the existing project style: bold-prefix summary, parenthetical row-id citations, file:function references where useful. Execution session copies this verbatim into `CHANGELOG.md` `[Unreleased]` section. Pattern (see existing entries for tone): `**<one-line problem statement> (\`row-id-1\`, \`row-id-2\`).** <root-cause sentence with file:line>. <fix sentence with the new helper / parameter / pattern>. <test/verification sentence>.` |
| **Backlog sync** | "Close rows: [ids]. Mark obsolete: [ids if any]. Update related: [adjacent rows that need their cross-refs adjusted]." |

## Step 5 — Sort and output

Sort initiatives by:

1. Highest correctness risk class present in the bundle (P2 > P3-correctness > P3-UX > P4).
2. Rows-closed multiplier (higher first) — reward consolidation.
3. Implementation cost (lower first).

**Tie-break rule:** when a heroic-cost initiative ties with medium-cost correctness
fixes, schedule the heroic one **last**. Ship the compounding wins first so later
sessions benefit from an improved surface area. Surface the heroic one in the plan
clearly marked `schedule: heroic-last`.

## Step 6 — Write outputs

Produce TWO files at a time-stamped plan directory:

**Path:** `ai_docs/plans/{YYYYMMDDTHHMMSSZ}_backlog-sweep/`

- `plan.md` — one initiative-plan table (from Step 4) per initiative, in scheduled
  order. Append a final todo: `backlog: sync ai_docs/backlog.md` per the backlog
  agent contract (see `ai_docs/workflow.md` § Backlog closure).
- `state.json` — machine-readable initiative status (schema below). Used by execution
  sessions to pick up the next pending initiative.

Do NOT write any code. This is planning only.

## state.json schema

```json
{
  "planPath": "ai_docs/plans/20260415T140000Z_backlog-sweep/plan.md",
  "createdAt": "ISO-8601 UTC",
  "bootstrapCaveat": true,
  "initiatives": [
    {
      "id": "output-size-summary-mode-family",
      "title": "Add summary=true mode to response-size-critical tools",
      "order": 1,
      "backlogRowsClosed": [
        "find-references-preview-text-inflates-response",
        "symbol-impact-sweep-output-size-blowup",
        "get-nuget-dependencies-no-summary-mode",
        "get-syntax-tree-max-output-chars-incomplete-cap",
        "validate-workspace-output-cap-summary-mode"
      ],
      "rowsClosedCount": 5,
      "status": "pending",
      "correctnessClass": "P3-UX",
      "scheduleHint": null,
      "changelogCategory": "Added",
      "branch": null,
      "worktreePath": null,
      "prUrl": null,
      "mergedAt": null,
      "notes": ""
    }
  ]
}
```

Field reference:

- `status`: `pending` | `in-progress` | `in-review` | `merged` | `obsolete` | `deferred`.
- `correctnessClass`: `P2` | `P3-correctness` | `P3-UX` | `P4`. Drives sort and gating.
- `scheduleHint`: `null` or `"heroic-last"`.
- `changelogCategory`: `Fixed` | `Added` | `Changed — BREAKING` | `Maintenance`.

## Final checks before exit

- [ ] Every shortlisted backlog row appears in exactly one initiative's
      `backlogRowsClosed` (or is explicitly marked `deferred` with a reason).
- [ ] Every initiative has a CHANGELOG entry draft (Step 4 field).
- [ ] state.json `initiatives` is sorted by `order` ascending.
- [ ] plan.md ends with `backlog: sync ai_docs/backlog.md` todo.
- [ ] You have NOT modified `backlog.md` or `CHANGELOG.md` directly. That's execution's
      job, in the same PR as the implementation, per `workflow.md`.
