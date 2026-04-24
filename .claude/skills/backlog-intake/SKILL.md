---
name: backlog-intake
description: "Consolidate deep-review artifacts (mcp-server-audit, experimental-promotion, roslyn-mcp-retro) into ai_docs/backlog.md with anchor verification, dedupe, priority classification, and Rule 1/3/4/5 sizing for the backlog-sweep-plan.md planner prompt. Use when: a deep-review / audit wave has produced `*_mcp-server-audit.md` / `*_roslyn-mcp-retro.md` / `*_experimental-promotion.md` files across sibling repos (or this one) and you want them reviewed, deduped, and merged into the backlog as properly-sized initiative rows."
user-invocable: true
argument-hint: "[--stage | --skip-verify | --no-commit] — defaults: stage files first, verify against CHANGELOG, commit on a fresh branch"
---

# Backlog intake

You are triaging a batch of deep-review artifacts into `ai_docs/backlog.md`. Your job is to turn raw audit / retro / experimental-promotion markdown files into **properly-sized, anchor-verified, de-duplicated, priority-ranked** rows that the `backlog-sweep-plan.md` planner prompt can plan against without re-processing.

This skill replaces the `eng/new-deep-review-batch.ps1 → sync-deep-review-backlog.ps1` pipeline. The PowerShell pipeline handled only `*_mcp-server-audit.md`, did literal-text dedupe, and couldn't verify anchors or size rows to Rule 1/3/4. The judgment work belongs in an LLM; the mechanical file-staging is delegated to `eng/stage-review-inbox.ps1`.

## Server discovery

This skill edits repository files, shells out to `pwsh` / `gh` / `git`, and uses Roslyn MCP **read-only** tools (`symbol_search`, `find_references`, `get_source_text`) to verify anchors. No `*_apply` / `*_preview` writers. If you find yourself calling a writer, you are in the wrong skill — stop and hand back.

## Input

`$ARGUMENTS` is optional. Supported flags:

| Flag | Effect |
|---|---|
| `--stage` | Explicit staging pass: always run `eng/stage-review-inbox.ps1` before triage, even if `review-inbox/` already has files. |
| `--skip-stage` | Skip staging entirely — triage whatever is already in `review-inbox/`. |
| `--skip-verify` | Skip the "already shipped?" cross-check against CHANGELOG + recent plans. Faster but riskier. |
| `--no-commit` | Write the updated `ai_docs/backlog.md` but do not create a branch or commit. |
| `--sibling-parent <path>` | Override the sibling-repo scan root (forwarded to the PS1). |

Default (no flags): stage if `review-inbox/` is empty, verify against CHANGELOG, commit to a fresh branch off `main`.

## Preconditions (HARD GATES — refuse if any fail)

1. **`git` CLI on PATH.** If not, refuse.
2. **`pwsh` CLI on PATH** (used by the staging script). If not, refuse.
3. **Working tree clean** OR the only dirty paths are `review-inbox/` + `ai_docs/backlog.md`. If other paths are dirty, refuse: `"Working tree has unrelated changes — commit or stash before intake."`
4. **`main` up to date with `origin/main`** (`git fetch origin main` first). Refuse if `main` diverged in a way that requires manual resolution.
5. **`ai_docs/backlog.md` exists** with the `## P2 / P3 / P4 — open work` + `## Refs` structure. If the file is missing or shape-wrong, refuse: `"backlog.md shape is unexpected — hand edit before re-running this skill."`

## Workflow

### Phase 0 — Stage artifacts

Skip this phase if `--skip-stage` is set.

```bash
pwsh -File eng/stage-review-inbox.ps1 -DryRun
```

Review the dry-run output. If the file list looks right:

```bash
pwsh -File eng/stage-review-inbox.ps1
```

If `--stage` was passed OR `review-inbox/` is empty at the start of the run, also run the real command. If the PS1 finishes with "No new artifacts found" AND `review-inbox/` is empty, refuse: `"Nothing to triage. Produce audits first via ai_docs/prompts/deep-review-and-refactor.md, then re-run."`

Record the count: `N files staged from M repos` for the final summary.

### Phase 1 — Extract actionable items (subagent)

The `review-inbox/` files are typically 200-800 lines each, totaling several thousand lines. Extraction happens in a **subagent** to protect the main context. Use `subagent_type: "general-purpose"`.

Subagent prompt template (fill the bracketed slots):

> You are extracting actionable items from markdown reports under `{repo-root}/review-inbox/` and normalizing them into backlog rows for `ai_docs/backlog.md`.
>
> **Context.** The Roslyn-Backed-MCP repo is a C# MCP server that exposes Roslyn-powered tools (refactor, analyze, review, …) to Claude Code. It is consumed by several sibling C# repos. Reports come in three shapes:
> - `*_mcp-server-audit.md` — audits where the server's tools are exercised against a real codebase. Findings split between "bug in the audited codebase" and "gap/limitation in the MCP server tools."
> - `*_experimental-promotion.md` — audits of whether an experimental feature is ready for promotion. Findings often name the MCP tool the feature depends on.
> - `*_roslyn-mcp-retro.md` — session retrospectives on using the MCP server. Richest source of items actionable in THIS repo.
>
> **Scope.** `ai_docs/backlog.md` is ONLY for work actionable inside the Roslyn-Backed-MCP repo. **Include**: MCP-server bugs, tool gaps, service-layer fixes, skill/CLI/docs/test/build/release work. **Exclude**: bugs/refactors inside the consumer repos (those belong in the consumer's own backlog). Retro observations ("tool X crashed", "tool Y missed Z", "docs unclear") ARE items for this repo.
>
> **Your task.** (1) Read every file under `review-inbox/`. (2) Extract every actionable item (explicit recommendations, "should", "gap", "missing", "blocker", "next step"). (3) Filter to items actionable in this repo. (4) **Deduplicate aggressively** across files — if 4 retros say the same thing, it is ONE row. (5) Classify rough area (workspace mgmt, refactor tools, analysis, etc.) — for grouping, not a column. (6) Rank P2/P3/P4:
>   - **P2**: tool broken for its core purpose, blocks ship, correctness bug with silent bad output.
>   - **P3**: meaningful friction / gap that consistently hurts workflow quality; real fix needed.
>   - **P4**: polish, nice-to-have, edge case, speculative.
>
> (7) Cross-check each new row against the existing `ai_docs/backlog.md` P2 / P3 / P4 tables — if an item overlaps, note as "refine existing row `<id>`" rather than a new row.
>
> **Deliverable.** One markdown response:
> - `### New rows to add` — a table `| id | pri | deps | do |` sorted P2 → P3 → P4, alphabetical by id within band. For each `do`, name specific tool / service / file anchors (not just "improve X").
> - `### Notes` — overlaps with existing rows, items deliberately excluded, dedupe ratio.
>
> Target 15–35 rows after dedupe. If you're producing 60+, dedupe harder.

Expect the subagent to return a structured block. Capture the row list for Phase 2.

### Phase 2 — Verify-not-already-shipped

Skip this phase if `--skip-verify` is set.

For each candidate row, cross-check:

1. **`CHANGELOG.md` [Unreleased] + last 3 versions** — grep for the tool name, service, or symbol the row cites.
2. **Newest backlog-sweep plan** under `ai_docs/plans/*_backlog-sweep/` — read its `state.json` and `plan.md`. Does any shipped initiative describe the same fix?
3. **Git log, last 100 commits** — `git log --oneline -n 100 | grep -i <keyword>` on tool / service / symbol names.
4. **Code spot-check for rows flagged by 1-3** — open the named service / tool file and confirm the specific behavior still reproduces. For example, if the row says `symbol_search("")` overflows, grep `SymbolSearchService.cs` for an empty-query guard.

Don't spot-check every row's code — only the ones where prior evidence suggests the fix may have landed. Budget: ~1 minute per candidate flagged, ~0 for rows with no CHANGELOG / plan / log hit.

If this phase finds a row already shipped, **drop it** from the candidate list and record "dropped (already shipped per <evidence>)" in the final summary.

### Phase 3 — Fix anchors

For each remaining row, verify every service class, tool file, and file:line anchor resolves:

1. For service-class names (e.g. `DeadCodeService`, `CodeActionService`): confirm the `.cs` file exists at the cited path. If the row says `src/RoslynMcp.Roslyn/Services/FooService.cs` but only `src/RoslynMcp.Roslyn/Services/BarService.cs` exists, rewrite the row to cite the real file. **Common mistakes the extraction subagent makes**:
   - `CodeActionsService` (plural) → actual is `CodeActionService.cs`
   - `MoveTypeService` → actual is `TypeMoveService.cs`
   - `SemanticSearchService` → logic lives in `CodePatternAnalyzer.cs`
   - Hallucinated `ServerInfoService` / `PackageReferenceService` / `RevertService` — tools-layer-only, see `src/RoslynMcp.Host.Stdio/Tools/` or the unified service (e.g. `UndoService.cs` for revert).
2. For tool registrations (e.g. `find_references_bulk`): grep `src/RoslynMcp.Host.Stdio/Tools/` for `Name = "<tool_name>"` and cite the real file + line. The PS1 pipeline NEVER did this; it's a distinctive value-add of this skill.
3. For file:line references (e.g. `CompileCheckService.cs:155`): run `get_source_text` on that span to confirm the referenced code is still there.

Rewrite each row's `do` text to cite **both** the core service (under `src/RoslynMcp.Roslyn/Services/`) **and** the tool registration (under `src/RoslynMcp.Host.Stdio/Tools/`). Executors can then land on the right file immediately.

If an anchor genuinely cannot be resolved, tag the row with `[stale — cited anchor not found; executor may use synthetic examples]` per `backlog-sweep-plan.md` Step 3's anchor-verification guidance, rather than dropping the row.

### Phase 4 — Split heroic rows

Apply `backlog-sweep-plan.md` Rule 1 to each row. A row is **heroic** (and must be split) if any of:

- It describes two or more distinct bugs that live in **different code paths** (different functions, different files).
- It asks for ≥4 production-file edits to fulfill.
- Its regression tests are not trivially-additive variants of one shape.
- Its "do" field contains a numbered "(1) fix A, (2) fix B, (3) fix C" list where each item is a different code change.

Common heroic shapes to watch for:

| Shape | Split strategy |
|---|---|
| "Fix tool X has 3 issues: schema drift + perf + empty data field" | Three rows, one per concern. Often different priorities (schema=P3, perf=P3, empty=P4). |
| "Scaffolder emits invalid C# because (a) identifier bug, (b) static target, (c) ctor args" | Three rows — each touches a different code path in the service. |
| "Tool A and Tool B both need summary-mode paging" | Two rows — different tool files, different handlers, even if the shape is similar. |
| "Guard X in WorkspaceManager AND in every tool wrapper" | Tighten to the single choke point; usually WorkspaceManager alone covers all callers. |

For each split, give each child a distinct kebab-case id (prefix with the original id where readable — e.g. `scaffold-test-preview-dotted-identifier` / `-static-target-body` / `-ctor-arg-stubs`).

### Phase 5 — Ensure planner-prompt refs

Before writing, verify the Refs table in `ai_docs/backlog.md` includes these entries (add any missing):

| Path | Role (template text) |
|---|---|
| `ai_docs/prompts/backlog-sweep-plan.md` | Planner prompt; enforces per-initiative Rule 1 (bundle only on shared code path) / Rule 3 (≤4 prod files) / Rule 3b (toolPolicy) / Rule 4 (≤3 test files) / Rule 5 (≤80K context). |
| `ai_docs/prompts/backlog-sweep-execute.md` | Executor companion; consumes the planner's `state.json` and vets each initiative against Rules 3/4/5. |
| `ai_docs/bootstrap-read-tool-primer.md` | Self-edit read-only tool primer (Roslyn-MCP read-side tools preferred over Bash/Grep). |
| `ai_docs/runtime.md` | Bootstrap scope policy — main-checkout self-edit (no `*_apply`) vs worktree/parallel-subagent sessions. |
| `review-inbox/` | Source evidence for the open rows. Keep until each row is closed or superseded. |

### Phase 6 — Write backlog.md and commit

1. Update `updated_at:` to current UTC.
2. Re-sort each priority band alphabetically by id.
3. Ensure the "Standing rules" section includes the initiative-sizing rule: `"Size every row to a single backlog-sweep-plan.md initiative: one code path, ≤4 production files, ≤3 test files, one regression-test shape."` (add if missing).
4. Skip if `--no-commit`; otherwise:
   ```bash
   git switch main
   git pull --ff-only
   git switch -c chore/backlog-audit-intake-{YYYYMMDD}
   git add ai_docs/backlog.md review-inbox/
   git commit -m "..."
   ```
   Commit message template:
   ```
   chore(backlog): intake cross-repo audit + retro batch ({YYYY-MM-DD})

   Consolidates actionable items from {N} review files across {M} repos into
   ai_docs/backlog.md.

   Backlog now: {P2-count} P2 + {P3-count} P3 + {P4-count} P4 = {total} open
   rows (up from {prior}). Rows anchored to both core services (src/RoslynMcp.Roslyn/Services/)
   and tool registrations (src/RoslynMcp.Host.Stdio/Tools/) so first executor lands
   on the right file without a scavenger hunt.

   - Splits applied: {list of heroic rows that were split}
   - Dropped (already shipped): {list, if any}
   - Verified against CHANGELOG [Unreleased] + last 3 versions + newest backlog-sweep plan.

   Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
   ```
5. Do NOT push unless the user asked. Leave the branch local.

### Phase 7 — Report

Emit a final summary:

```
Backlog intake complete.
  Staged: {N} files from {M} repos (review-inbox/)
  Extracted: {raw} candidate items
  Deduped: {deduped} rows ({ratio}:1 dedupe)
  Dropped (already shipped): {shipped-count}
  Anchor fixes applied: {anchor-fix-count}
  Heroic splits: {split-count} rows → {split-total}
  Final: {P2} P2 + {P3} P3 + {P4} P4 = {total} open rows
  Commit: {SHA} on branch {branch-name} (local-only; push when ready)
```

## Refusal cases (explicit)

- **`review-inbox/` empty AND staging found nothing** → refuse per Phase 0.
- **Working tree dirty beyond backlog.md + review-inbox/** → refuse per Precondition 3.
- **`backlog.md` shape unexpected** → refuse per Precondition 5.
- **Subagent returns 0 rows** → do NOT write an empty commit. Report "no actionable items extracted from {N} files" and exit.
- **Subagent returns > 60 rows after dedupe** → stop and ask the user whether to proceed with a clearly-under-deduped result, rather than silently writing a heroic backlog.

## Why a subagent for Phase 1

Each review file is 200-800 lines; a 14-file batch is 6000+ lines. Reading them all in-context would consume 30-50K tokens before any judgment work starts. The extraction subagent returns a compact structured list (typically 1-2K tokens), leaving the main agent's context free for the anchor-verify / heroic-split / commit phases, which require reading this repo's source files.

If the batch is small (≤3 files, ≤1000 total lines), skip the subagent and read directly — the overhead isn't worth it.

## Distinct from related skills

- **`close-backlog-rows`**: removes specific row ids from the open table after their PRs ship. This skill (`backlog-intake`) ADDS rows from raw audit evidence. Use `close-backlog-rows` after a ship, this one after an audit wave.
- **`reconcile-backlog-sweep-plan`**: updates a backlog-sweep plan's `state.json` when its PRs have merged. Different file, different concern.
- **`draft-changelog-entry`**: drafts one changelog fragment per PR from commit metadata. Different artifact.
- **`backlog-sweep-plan.md` (prompt, not skill)**: consumes the backlog this skill produces. Run `backlog-intake` first, then the sweep planner.

## Historical note

This skill replaces five PowerShell scripts that were deleted when it shipped: `compare-external-audit-sources.ps1`, `import-deep-review-audit.ps1`, `new-deep-review-batch.ps1`, `new-deep-review-rollup.ps1`, `sync-deep-review-backlog.ps1`. The last (the "sync" script) was the lossiest — it did literal-text dedupe via PowerShell regex and couldn't verify anchors, dedupe semantically, cross-check against CHANGELOG, or size rows to Rule 1/3/4. The only piece worth keeping from the pipeline was file staging, which is now a 150-line `eng/stage-review-inbox.ps1`.
