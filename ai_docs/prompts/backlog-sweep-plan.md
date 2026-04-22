# Backlog sweep — planning pass

<!-- purpose: Initiative-level plan that groups related backlog rows into shippable PRs.
     v3 (2026-04-17) — adds (a) Rule 3 "new-MCP-tool structural-unit" exemption after
     2026-04-17 pass 2 Init 4 shipped 7 prod files against a 2-prod estimate because
     the Core+Roslyn+Host.Stdio three-layer pattern is indivisible, and (b) a per-
     initiative toolPolicy field (`edit-only` / `preview-then-apply` / null) consumed
     by the v4 executor's parallel-mode Step 5 pre-classification. state.json
     schemaVersion stays at 2 (toolPolicy is optional; null → executor defaults).
     Bootstrap caveat clarified: *_apply forbidden in main-checkout self-edit only;
     worktree sessions (serial or parallel subagents) may use *_apply freely.
     Prior revisions: v2 (2026-04-16) forced strict per-initiative scope discipline
     after v1 over-bundled by treating "shared root concept" as "shared fix shape". -->

You are a senior engineer working in the Roslyn-Backed-MCP repo. Your job is to read
`ai_docs/backlog.md` and produce a per-initiative implementation plan that is **honest
about per-initiative cost** and produces initiatives a single executor session can
realistically finish.

Companion: `backlog-sweep-execute.md` consumes the `state.json` you produce here. The
executor has a **vetting step** that will refuse to start work on initiatives that
violate the scope rules below — so produce a plan that passes vetting.

## Project constraints

- **No backward compatibility requirements.** Breaking changes to public APIs, schemas,
  response shapes, parameter names, and internal contracts are acceptable.
- **Large refactors are on the table** — but each refactor is its own initiative.
- **Priorities, strict order:** correctness → performance → SRP. Correctness always
  wins. When correctness risk ties, use blast radius, then SRP improvement, then cost.
- **Bootstrap caveat (main-checkout self-edit only — NOT worktrees).** This codebase
  is the Roslyn MCP server itself. In the **main checkout**, the binary servicing
  the call is the binary being edited, so `*_apply` mutates the MSBuildWorkspace
  snapshot the running server is holding — avoid `*_apply` / `*_preview` self-
  application in main-checkout sessions. **Worktree sessions (serial execution in
  `.worktrees/<id>/` OR parallel-mode subagents) are NOT bootstrap** — they operate
  against the **installed global tool** at `%USERPROFILE%\.dotnet\tools\roslynmcp.exe`,
  which is a distinct artifact that is NOT mutated by worktree-source edits. Worktree
  sessions may use `*_apply` freely when the initiative's toolPolicy is
  `preview-then-apply` (see Rule 3 toolPolicy below). **Read-side Roslyn MCP tools
  are always preferred** over Bash/Grep regardless of mode: `compile_check` /
  `test_run` / `find_references` / `symbol_search` / `document_symbols`. See
  `ai_docs/bootstrap-read-tool-primer.md` for the pattern→tool table,
  `ai_docs/runtime.md` § *Bootstrap scope — self-edit on THIS repository* for the
  two-sub-case policy, and `self-edit-bootstrap-mode-mcp-development` +
  `bootstrap-read-only-roslyn-mcp-checklist-for-self-edit-sessions` in backlog for
  prior-session evidence.

## Scope discipline rules (HARD)

These rules are enforced by the executor's vetting step. Plans that violate them will
be rejected and you'll be asked to re-plan.

### Rule 1 — Default: one row = one initiative

Every backlog row is its own initiative unless **all four** of the following are
explicitly verified true via source reads (cite file:line in the plan):

1. The rows touch the **same code path** (same function or same tightly-coupled set of
   functions in one file).
2. The fix is a **single change** to that path that resolves all symptoms.
3. The **regression test for one row also exercises the others** (or the tests are
   trivially-additive variants of one shape).
4. The bundle still respects the file-count budget in Rule 3.

If any of the four is uncertain, the rows are **separate initiatives**. Do not bundle
on theme, root-concept, or shared infrastructure unless the four conditions hold.

### Rule 2 — Source-verify before bundling

Before claiming two rows can be bundled, you must:

- Read the cited source files for both rows.
- Confirm the failure mode is in the same code path. "Both touch the formatter" is NOT
  enough — verify the actual buggy emit happens in the same function.
- Note in the initiative's Diagnosis field: "Bundled with row X because [specific shared
  code path at file:line]; one fix resolves both."

### Rule 3 — Per-initiative file-count budget

An initiative must touch **≤ 4 production files** (excluding tests, CHANGELOG.md,
ai_docs/backlog.md, and the plan/state.json triple). If more files are needed, **split**.

This is a hard upper bound. Most fix/refactor initiatives should touch 1-2 files.

**Rule 3 exemption — new-MCP-tool structural unit.** Initiatives that introduce a
new `[McpServerTool]` method following the repo's Core+Roslyn+Host.Stdio three-layer
pattern count **structural units**, not files. The indivisible new-tool shape is:

| Structural unit | Typical files |
|---|---|
| Core contract | `src/RoslynMcp.Core/Services/I{Tool}Service.cs` + `src/RoslynMcp.Core/Models/{Tool}Result.cs` (+ any request DTO) |
| Roslyn implementation | `src/RoslynMcp.Roslyn/Services/{Tool}Service.cs` |
| Host.Stdio tool surface | `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs` |
| Registration | `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` entry (forced by the RMCP001/RMCP002 analyzer) + `ServiceCollectionExtensions.cs` DI line |

For new-tool initiatives, Rule 3's cap is **≤ 4 structural units** (typically 5-7
files). The fix/refactor cap (≤ 4 production files) still applies to initiatives
that don't introduce a new tool. Record `productionFilesTouched` as the **file
count**, not the unit count, so the executor's vetting step sees the real file
budget. Plans for new-tool initiatives MUST set `toolPolicy: "edit-only"` and cite
the structural-unit exemption in the initiative's Scope field.

**Fixed-cost addenda — new-MCP-tool initiatives MUST include these in the file
count (typically +3 files on top of the 4 structural units):**

| Addendum | File | Why |
|---|---|---|
| Test-fixture DI | `tests/RoslynMcp.Tests/TestBase.cs` | Register the new `I{Tool}Service` so fixture `ServiceProvider` can resolve it; otherwise tests that request the service via DI fail at resolution time. |
| Test-fixture DI (container-style) | `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs` | Same, for fixtures that build a `TestServiceContainer` instead of inheriting `TestBase` directly. |
| README surface-count | `README.md` | The `ReadmeSurfaceCountTests` gate (shipped PR #294) asserts `README.md`'s "N tools (X stable / Y experimental)" matches `ServerSurfaceCatalog` at test time. A new Experimental tool bumps `Y` by 1 and `N` by 1; a new Stable tool bumps `X` and `N`. |

These addenda are mechanical consequences of the new-tool shape but are NOT a
5th structural unit — a plan that genuinely needs > 4 structural units must still
split. Cite the addenda in the initiative's Scope field alongside the structural
units so the executor's vetting step sees the full file budget up front.

**Session evidence (structural units):** 2026-04-17 pass 2 Init 4
(`exception-handler-classification-tracer`) shipped 7 prod + 1 test against a
2-prod estimate because the new-tool shape forced the three-layer pattern. See
backlog row `backlog-sweep-plan-rule3-new-service-5-file-unit` and the merged PR
#239 file list.

**Session evidence (addenda):** 2026-04-22 `compilation-prewarm-on-load` (PR #323)
shipped 7 prod + 1 test against a 6-prod estimate because the plan missed the 3
addenda above — all 3 surfaced at validation time and had to be added in the
executor worktree.

### Rule 3b — toolPolicy per initiative

Every initiative gets a `toolPolicy` value that the executor uses to brief the
subagent (parallel mode) or set defaults (serial mode):

| toolPolicy | When to use | Implementation path |
|---|---|---|
| `"edit-only"` | Adding/modifying files where the diff is textual and local. Covers: new-MCP-tool shape, fix/refactor ≤ 3 files, doc-only, config-only, DTO field addition. | Subagent uses `Edit`/`Write`/`Read` + read-side MCP. No `*_apply`. |
| `"preview-then-apply"` | Solution-wide symbolic refactor: rename across projects, extract-interface-and-wire, bulk-type-replacement, move-type-to-project, code-fix-all. | Subagent calls `*_preview` then `*_apply` in the same turn. Preview redemption lives in-conversation. |
| `null` | You are not sure. | Executor defaults: edit-only for ≤ 3 production files; preview-then-apply otherwise. Prefer explicit classification when you can — it saves the executor a Step-4 inference pass. |

Classify at planning time based on the Approach field. If the Approach calls for
a rename, extract, or bulk-replace against an existing symbol set, `"preview-then-
apply"` is usually correct. If the Approach is "create these N new files and wire
them up," `"edit-only"` is always correct (new files have no consumers to rewrite
semantically).

### Rule 4 — Per-initiative test budget

An initiative adds **≤ 3 new test files** (or extends ≤ 3 existing test files with new
test methods). If a fix legitimately needs more, the initiative is too broad — split.

### Rule 5 — Context-cost estimate per initiative

For each initiative, estimate the executor session's context cost:

| Activity | Cost per occurrence |
|---|---|
| Read a service file (300-800 lines) | ~3-8K tokens |
| Read a helper / contract file | ~1-3K tokens |
| Edit a file | ~1-2K tokens (small) to ~5K (large) |
| Add or modify a test file | ~3-8K tokens |
| Write a new file (no Read first) | ~2-4K tokens |
| Build cycle (`dotnet build`) | ~2-5K tokens output |
| `compile_check` via MCP | ~0.5-1K tokens output |
| `test_run --filter` via MCP | ~1-3K tokens output |
| `verify-release.ps1` run | ~5-15K tokens output |
| Ship pipeline (commit, PR, merge, cleanup) | ~3-5K tokens |
| **New-MCP-tool 3-layer bundle** (Core + Roslyn + Host.Stdio writes + catalog entry + DI) | **~15-25K tokens** |
| **Rebase on parallel-mode peer PR** (CHANGELOG/backlog.md conflict resolve) | ~2-4K tokens |

Per-initiative budgets, by shape:

| Initiative shape | Typical estimate |
|---|---|
| Doc-only / config-only | ~15-25K tokens |
| Fix/refactor, 1-2 files | ~25-40K tokens |
| Fix/refactor, 3-4 files | ~40-60K tokens |
| New-MCP-tool (edit-only, structural-unit exemption) | ~45-65K tokens |
| Solution-wide rename/extract (preview-then-apply) | ~50-70K tokens |

Initiatives over 80K must split. Record the estimate in `estimatedContextTokens`.

The executor vets this number — if your estimate is over budget, the executor will
refuse to start.

## Step 1 — Candidate selection (row-level)

Read `ai_docs/backlog.md` (open/unfinished work only). Shortlist rows by:

1. **Correctness risk** — items where current behavior silently corrupts user data or
   produces wrong results rank highest (P2 first, then P3 correctness-flavored rows).
2. **Blast radius** — how many tools, components, or consumer workflows are affected.
3. **Feasibility** — can be completed in a single focused PR within Rules 3-5. Skip
   rows whose `deps` field references unfinished work.

Do NOT cap at an item count. Go deep enough to cover every P2 + all P3 rows + the
highest-impact P4 rows. The output is the initiative count, which after Rules 1-5
will likely be **close to 1:1 with row count**.

## Step 2 — Source-verified consolidation (rare)

For rows that look like they might bundle:

- Read the source for both/all rows in the candidate bundle.
- Run through Rule 1's four conditions. ALL must hold.
- If they hold: bundle, cite the shared code path explicitly.
- If they don't: keep separate. Note any shared infrastructure (e.g., "both will use
  the new SyntaxFormatter helper") in each initiative's Approach but ship separately.

**Anti-pattern to avoid:** "Same root concept" bundling. Examples that should NOT
bundle (verified by past audit):
- `formatter-format-async-wiring` family — every preview service has band-aid trivia
  comments warning AGAINST blanket reformatting. Each service is its own initiative.
- `output-size-summary-mode-family` — each tool's response shape is different; each
  needs its own DTO change + tool-schema update. Each is its own initiative.

When in doubt, do NOT bundle.

## Step 3 — Independent verification (per initiative)

For each initiative, do NOT trust the backlog text as ground truth. It may be stale.
Verify:

- Read the actual source files cited. Confirm the behavior still exists.
- Use `roslyn-mcp:review`, `roslyn-mcp:complexity`, `roslyn-mcp:dead-code` READ-ONLY
  skills against the touched code. Cite their output if useful.
- If the behavior no longer reproduces: drop the row, mark `obsolete: <id>` in the
  initiative's Backlog sync field.
- Identify root cause in code, not just symptoms (file:line references).

**Anchor verification (SHOULD).** For each symbol, method, or helper name cited in an
initiative's Validation or Diagnosis field, run `mcp__roslyn__symbol_search` (or an
equivalent Grep pass) at plan-draft time to confirm the anchor still resolves. If
the anchor does not resolve, **rewrite the field** to
`[stale — cited anchor not found; executor may use synthetic examples]` rather than
dropping the initiative. The plan still ships; the executor is informed up front and
picks a fresh example. Budget: ~2K planner tokens per initiative with cited anchors.
A cost-constrained sweep MAY skip this pass, but the skip must be explicit — log it in
plan.md's preamble so the executor knows anchor freshness wasn't verified. Prevents
the 2026-04-18 pass-3 init-6 class of failure where `Build*Diff` helpers had
consolidated into `DiffGenerator.GenerateUnifiedDiff` between plan-draft and
plan-execute (`plan-anchor-examples-go-stale-between-plan-and-execute`).

**Adjacent perf smell check** on touched files: (a) repeated full-collection scans;
(b) sync enumeration of large data sets; (c) redundant work in loops; (d) over-reads.
Record findings. If a perf fix requires touching files outside the correctness work,
**spin it off as a new backlog row** — do not bundle.

## Step 4 — Initiative plan (per initiative)

Produce this table for each initiative:

| Field | Content |
|-------|---------|
| **Initiative id** | Kebab-case, grep-friendly. **One row's id is fine** — bundles get a kebab-case bundle name. |
| **Status** | `pending` initially. The executor updates this on every state transition so plan.md remains a self-contained recovery source. Format examples: `pending` · `in-progress (branch: remediation/foo)` · `in-review (PR #163)` · `merged (PR #163, 2026-04-17)` · `obsolete (no longer reproducible — see notes)`. |
| **Backlog rows closed** | List of row ids. Usually 1. Bundle only if Rule 1 conditions all hold (cite at the bottom of Diagnosis). |
| **Diagnosis** | What you found in the code (file:line). How it differs from or confirms the backlog description. If bundled, the explicit Rule-1 verification. |
| **Approach** | Concrete implementation strategy. Name files to create/modify, functions to change, patterns to follow. |
| **Scope** | Production files touched (count + list), test files added/modified (count + list), files deleted (if any). Must satisfy Rules 3 + 4. For new-MCP-tool initiatives, cite the Rule 3 structural-unit exemption and list the 4 structural units. |
| **Tool policy** | `edit-only` / `preview-then-apply` / null. Per Rule 3b. null is acceptable only when the Approach is genuinely ambiguous — prefer explicit classification. |
| **Estimated context cost** | Single number in tokens. Per Rule 5 — match the initiative shape's budget row. |
| **Risks** | What could go wrong; what adjacent behavior to verify. |
| **Validation** | How to confirm the fix: specific test cases, build checks, manual reproduction steps. |
| **Performance review** | ONLY if the fix touches a hot path. Otherwise: "N/A — correctness fix, no hot-path changes." |
| **CHANGELOG category** | `Fixed`, `Added`, `Changed — BREAKING`, or `Maintenance`. |
| **CHANGELOG entry (draft)** | Pre-drafted entry text in project style: bold-prefix summary, parenthetical row-id citations, file:function references. |
| **Backlog sync** | "Close rows: [ids]. Mark obsolete: [ids]. Update related: [ids]." |

## Step 5 — Sort and output

Sort initiatives by:

1. Highest correctness risk class present (P2 > P3-correctness > P3-UX > P4).
2. **Estimated context cost ASC within band** — cheapest first. Compounds session
   throughput; agents complete more per session when small wins are scheduled early.
3. Implementation interdependency — if initiative B's fix conflicts with initiative A,
   schedule A first.

**Tie-break for heroic-cost initiatives:** schedule LAST regardless of correctness
class, marked `scheduleHint: "heroic-last"`. The executor enforces that all non-heroic
initiatives ship before any heroic one runs.

## Step 6 — Self-vet before output

Before writing files, walk every initiative and confirm:

- [ ] No initiative claims to close more than 1 row UNLESS Rule 1's four conditions
      are explicitly cited via file:line evidence in Diagnosis.
- [ ] No initiative touches more than 4 production files (Rule 3 fix/refactor cap)
      OR more than 4 structural units (Rule 3 new-MCP-tool exemption, with the
      exemption explicitly cited in Scope).
- [ ] No initiative adds more than 3 test files (Rule 4).
- [ ] Every initiative has an `estimatedContextTokens` ≤ 80K (Rule 5), matched to
      its shape's budget row.
- [ ] Every initiative has a `toolPolicy` set (Rule 3b). Prefer explicit
      `edit-only` / `preview-then-apply` classification; null only when genuinely
      ambiguous. New-MCP-tool initiatives MUST be `edit-only`.
- [ ] Anchors in Validation/Diagnosis fields still resolve (Step 3 anchor-verification
      pass complete), OR the skip is explicitly logged in plan.md's preamble.
- [ ] At least one P2 or high-correctness P3 is in the top 5 by sort order.
- [ ] Total initiative count is honest about backlog size (~1:1 with row count is
      expected, not a sign of failure).

If any check fails, fix the offending initiatives BEFORE writing output files.

## Step 7 — Write outputs

Produce TWO files at a time-stamped plan directory:

**Path:** `ai_docs/plans/{YYYYMMDDTHHMMSSZ}_backlog-sweep/`

- `plan.md` — one initiative-plan table per initiative, in scheduled order. Append a
  final todo: `backlog: sync ai_docs/backlog.md` per the backlog agent contract.
- `state.json` — schema below.

Do NOT write any code. This is planning only.

## state.json schema

```json
{
  "planPath": "ai_docs/plans/YYYYMMDDTHHMMSSZ_backlog-sweep/plan.md",
  "createdAt": "ISO-8601 UTC",
  "schemaVersion": 2,
  "bootstrapCaveat": true,
  "completed": false,
  "completedAt": null,
  "initiatives": [
    {
      "id": "row-id-or-bundle-name",
      "title": "Short imperative title",
      "order": 1,
      "backlogRowsClosed": ["row-id-1"],
      "rowsClosedCount": 1,
      "estimatedContextTokens": 35000,
      "productionFilesTouched": 2,
      "testFilesAdded": 1,
      "toolPolicy": "edit-only",
      "status": "pending",
      "correctnessClass": "P3-correctness",
      "scheduleHint": null,
      "changelogCategory": "Fixed",
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

- `schemaVersion`: integer; current is `2`. Executor refuses plans with mismatched
  schema version. (Planner prompt is at v3 but the schema stays v2-compatible —
  `toolPolicy` is optional; missing field → executor defaults.)
- `status`: `pending` | `in-progress` | `in-review` | `merged` | `obsolete` | `deferred`.
- `correctnessClass`: `P2` | `P3-correctness` | `P3-UX` | `P4`. Drives sort and gating.
- `scheduleHint`: `null` or `"heroic-last"`.
- `changelogCategory`: `Fixed` | `Added` | `Changed — BREAKING` | `Maintenance`.
- `estimatedContextTokens`: per Rule 5; integer. Vetting fails if > 80000.
- `productionFilesTouched`: per Rule 3; integer. Fix/refactor vetting fails if > 4.
  New-MCP-tool exemption: executor checks initiative `notes` for the structural-unit
  citation and uses the 4-unit cap instead.
- `testFilesAdded`: per Rule 4; integer. Vetting fails if > 3.
- `toolPolicy`: per Rule 3b. `"edit-only"` | `"preview-then-apply"` | `null`.
  `null` means "executor decides based on file count and Approach shape." Optional
  for backward-compat with pre-v3 plans; planner v3+ should always emit explicitly.

## Final sanity check

You have NOT modified `backlog.md` or `CHANGELOG.md`. That's execution's job, in the
same PR as the implementation, per `workflow.md`. Confirm before exit.
