# Backlog sweep plan — 20260507T182128Z (revised 18:36 — split for Rule 3)

**Generated:** 2026-05-07T18:21:28Z
**Revised:** 2026-05-07T18:36:28Z
**Backlog snapshot:** 2026-05-07T20:30:00Z
**Initiative count:** 6
**Anchor verification:** performed
**Theme:** maintainer-audit-skill redesign (collapse-modes → relocate → update-refs → extract-skills-audit → fragment-pattern → per-repo-scorecard)

## Revision note

The previous version had 5 initiatives but two violated Rule 3 (initiative 1 needed a planner-introduced rename exemption; initiative 2 had an internal accounting contradiction at 5 files). This revision splits those into 3 strictly-Rule-3-compliant initiatives by **collapsing modes FIRST in the old location, before the move**. After mode collapse, the directory shrinks from 5 files to 3 (SKILL.md, prompt.md, archive-old-reports.ps1), so the subsequent relocate is mechanically a 3-file operation that fits within the cap with no exemption needed.

## Skipped rows

Same as previous version. Pre-existing P4/Low rows not in scope for this sweep:

- `change-signature-reorder-preview` — weak evidence
- `parameter-object-preview-tool` — design-doc-ready, not in current sweep theme
- `dry-run-preview-side-effect-audit` — investigation-first, weak evidence
- `promotion-scorecard-20260427-review` — defer until per-repo-scorecard ships (this sweep) which materially changes input
- `tool-surface-pagination-or-tool-sets` — track-only

## Initiatives (in order)

### 1. audit-deep-collapse-modes

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `mcp-server-stress-single-mode` |
| Diagnosis | The current skill has three modes (`full`, `promotion-only`, `read-only`) that are degenerate variants of one knob. `read-only` skips the scorecard (`prompts/read-only.md:20`) which makes the run nearly worthless — apply tools (`apply_text_edit`, `apply_with_verify`, `revert_last_apply`, preview→apply chains) are surface; preview-only runs leave the entire write path uncovered. The current Phase 6 (`prompts/full.md:21,642`) ships product changes through the audit pipeline, conflating audit with refactor. Fix: collapse to one mode, always exercise apply on a disposable worktree the skill creates and tears down post-run, always emit the scorecard. **Done in old location (`skills/audit-deep/`) so the directory shrinks from 5 files to 3 before the relocate in initiative 2 — keeps initiative 2 within Rule 3 without rename exemptions.** |
| Approach | (a) Edit `skills/audit-deep/SKILL.md` — Step 2 drops mode dispatch; Step 3 documents always-disposable-worktree; argument-hint becomes `[--no-worktree]`. (b) Rewrite + rename `skills/audit-deep/prompts/full.md` → `skills/audit-deep/prompts/prompt.md` — Phase 6 rewritten to exercise apply tools as test fixtures on a disposable worktree the skill creates at run start and tears down at run end via `dotnet build-server shutdown` + `git worktree remove --force` (per addenda's `worktree_lock_release`); audited repo's main is never touched. (c) Delete `skills/audit-deep/prompts/promotion-only.md`. (d) Delete `skills/audit-deep/prompts/read-only.md`. (e) Update `tests/RoslynMcp.Tests/Skills/AuditDeepSkillFrontmatterTests.cs` — drop the 3-mode assertion at lines 54-72; add: SKILL.md must NOT contain `mode=promotion-only` or `mode=read-only` tokens; SKILL.md must contain `--no-worktree` flag definition; SKILL.md must reference disposable-worktree teardown; mode-prompt path expectation goes from 3 paths to 1 (`prompts/prompt.md`). |
| Scope | **Production files: 4** — `skills/audit-deep/SKILL.md` (edit), `skills/audit-deep/prompts/prompt.md` (rename + content rewrite of full.md), `skills/audit-deep/prompts/promotion-only.md` (delete), `skills/audit-deep/prompts/read-only.md` (delete). **Test files: 1** — `AuditDeepSkillFrontmatterTests.cs`. Strict count: 4 prod files. Within Rule 3 with no exemption. |
| Tool policy | `edit-only` |
| Estimated context cost | 45000 |
| Risks | (a) Disposable-worktree teardown must run even on Phase 6 failure — wrap apply chain in `try/finally` discipline at the prompt level. (b) `dotnet build-server shutdown` discipline (per addenda) is mandatory before `git worktree remove --force` on Windows; document explicitly in the rewritten prompt. (c) The `--no-worktree` degraded-mode flag must record the gap in report header so consumers know which evidence is missing. (d) After this initiative ships and before initiative 2 ships, `.claude/skills/publish-preflight/SKILL.md` still references `/audit-deep mode=promotion-only` — that reference will be stale (the mode no longer exists). The publish-preflight skill is not user-invocable in steady state between sweep PRs, so this is acceptable; initiative 3 fixes it. |
| Validation | (1) `mcp__roslyn__compile_check` after the test edit. (2) `mcp__roslyn__test_run --filter "AuditDeepSkillFrontmatterTests"`. (3) Manual smoke: `/roslyn-mcp:audit-deep` (still in old location at this point) — confirm single-mode invocation creates worktree, applies run inside it, worktree is gone at end-of-run; `git worktree list` clean; `git status` in audited repo unchanged. (4) `./eng/verify-ai-docs.ps1` — links inside the rewritten prompt all resolve. |
| Performance review | N/A — prompt content rewrite, no hot path. |
| CHANGELOG category | Changed — BREAKING |
| CHANGELOG entry (draft) | Collapsed `/audit-deep` modes — `full`, `promotion-only`, `read-only` — into a single canonical run. Apply tools always exercised on a disposable worktree; promotion scorecard always emitted. The audited repo's working tree is never mutated. `--no-worktree` available for environments that can't create a worktree (degraded mode, recorded in report header). |
| Backlog sync | Close rows: `mcp-server-stress-single-mode`. (Row id was named based on planned final skill name; the work itself is mode collapse and is complete after this initiative ships.) |

### 2. mcp-server-stress-relocate

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | (none — paired with initiative 3) |
| Diagnosis | The skill at `skills/audit-deep/` is shipped to NuGet consumers via the Roslyn-MCP plugin. Its primary purpose is to audit *this repo's own* server surface, scorecard, and skills inventory (`prompts/full.md:11,50,552-570,671`) — none of which is useful to a normal consumer. Move it to `.claude/skills/mcp-server-stress/` (maintainer-only) following the same pattern as `.claude/skills/update/`, `publish-preflight/`, `release-cut/`. Rename to `mcp-server-stress` to pair with the existing static `surface-audit` skill (the dynamic-execution counterpart). After initiative 1 the source directory has only 3 files (SKILL.md, prompt.md, archive-old-reports.ps1); this initiative is purely mechanical relocation plus a SKILL.md frontmatter/description edit. |
| Approach | (a) `git mv skills/audit-deep .claude/skills/mcp-server-stress` — moves 3 files atomically: SKILL.md, prompts/prompt.md, scripts/archive-old-reports.ps1. (b) Edit the moved `.claude/skills/mcp-server-stress/SKILL.md` — frontmatter `name: audit-deep` → `name: mcp-server-stress`; description text rename `audit-deep` mentions → `mcp-server-stress`; intra-skill `${CLAUDE_PLUGIN_ROOT}/skills/audit-deep/...` references → `.claude/skills/mcp-server-stress/...`. (c) Update test files: `tests/RoslynMcp.Tests/Skills/AuditPhaseRunnerHandoffTests.cs` (lines 6, 79 — change path constant), `ArchiveOldReportsScriptTests.cs` (lines 7, 8, 10, 166 — change path constant), `AuditDeepSkillFrontmatterTests.cs` (line 28 `SkillName` constant `audit-deep` → `mcp-server-stress`, plus line-36 path; rename file + class to `McpServerStressSkillFrontmatterTests`). |
| Scope | **Production files: 3** — `.claude/skills/mcp-server-stress/SKILL.md` (move + content edit), `.claude/skills/mcp-server-stress/prompts/prompt.md` (move only, no content delta), `.claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1` (move only, no content delta). Strict count: 3 prod files (1 with content edit, 2 mechanical git-mv). Within Rule 3 with no exemption. **Test files: 3** — the three test files with path/name updates. |
| Tool policy | `edit-only` |
| Estimated context cost | 30000 |
| Risks | (a) Frontmatter `name:` field MUST match the new directory name `mcp-server-stress` or skill discovery breaks. (b) Test class rename (`AuditDeepSkillFrontmatterTests` → `McpServerStressSkillFrontmatterTests`) requires file rename — git-mv the test file to preserve history. (c) Until initiative 3 ships, `.claude/skills/publish-preflight/SKILL.md` and `.claude/agents/audit-phase-runner.md` still reference the old `/audit-deep` slash command + `skills/audit-deep/` paths — broken references for one PR cycle. publish-preflight isn't auto-invoked in steady state, and audit-phase-runner is invoked only by the audit skill itself which is mid-relocation; acceptable transient. |
| Validation | (1) `mcp__roslyn__compile_check` after each test edit. (2) `mcp__roslyn__test_run --filter "McpServerStressSkillFrontmatterTests"` — passes with new path/name/class. (3) `mcp__roslyn__test_run --filter "AuditPhaseRunnerHandoffTests"` and `ArchiveOldReportsScriptTests` — pass with new paths. (4) `./eng/verify-ai-docs.ps1`. (5) Manual: `/mcp-server-stress` is invocable from this repo; `/roslyn-mcp:audit-deep` no longer appears in shipped catalog (run `/surface-audit` to confirm). |
| Performance review | N/A — file moves, no hot path. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | (Combined with initiative 3 — single fragment for the relocate-and-rename row.) |
| Backlog sync | (Initiative 3 closes the row.) |

### 3. mcp-server-stress-update-external-refs

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `audit-deep-relocate-and-rename` |
| Diagnosis | After initiative 2, three external references still point at the old skill name + path: `.claude/skills/publish-preflight/SKILL.md` (5 mentions of `/audit-deep mode=...`), `.claude/agents/audit-phase-runner.md` (the agent the audit skill orchestrates), and the orphaned `ai_docs/prompts/deep-review-and-refactor.md` (a pre-bundled-prompt era artifact the shipped skill no longer reads). Updating these in a separate initiative keeps each within Rule 3; bundling with initiative 2 would push it to 6 production files. |
| Approach | (a) Edit `.claude/skills/publish-preflight/SKILL.md` lines 101, 109, 111, 112, 135 — replace `/audit-deep mode=promotion-only` with `/mcp-server-stress` (drop mode qualifier; default single-mode emits scorecard). (b) Edit `.claude/agents/audit-phase-runner.md` — rename references from `audit-deep` to `mcp-server-stress`; update any cited skill path. (c) Delete `ai_docs/prompts/deep-review-and-refactor.md` — no longer referenced by any skill; was the pre-bundled-prompt source. Verify with grep before deletion that no doc still links to it. |
| Scope | **Production files: 3** — `.claude/skills/publish-preflight/SKILL.md`, `.claude/agents/audit-phase-runner.md`, `ai_docs/prompts/deep-review-and-refactor.md` (delete). **Test files: 0**. Strict count: 3. Within Rule 3. |
| Tool policy | `edit-only` |
| Estimated context cost | 25000 |
| Risks | (a) `verify-ai-docs.ps1` may flag any doc that still links to the orphaned prompt — grep for `deep-review-and-refactor` before deletion; if any reference remains, update or delete the referrer first. (b) audit-phase-runner.md may have prose that describes the audit-deep skill's phases by name — content edits, not just rename, may be needed. (c) The 5 publish-preflight mentions are clustered (same paragraph); ensure all replacements happen in one edit pass. |
| Validation | (1) `./eng/verify-ai-docs.ps1` — no broken links from the orphan delete or rename. (2) `grep -r "audit-deep" .claude/ ai_docs/ docs/ README.md` — only allowed remaining mention is in CHANGELOG.md historical entries (those don't get rewritten). (3) `./eng/verify-release.ps1 -Configuration Release`. |
| Performance review | N/A — text edits, no hot path. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Moved the maintainer-only audit skill out of the shipped plugin surface. Renamed from `audit-deep` to `mcp-server-stress` and relocated to `.claude/skills/mcp-server-stress/`. The consumer plugin no longer ships the skill — it audited this repo's own server surface, scorecard, and skills inventory and was never useful outside this repo. (Combines initiatives 2 + 3 into one CHANGELOG fragment for the row.) |
| Backlog sync | Close rows: `audit-deep-relocate-and-rename`. |

### 4. extract-skills-audit-from-server-stress

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-skills-audit-from-server-stress` |
| Diagnosis | Phase 16b (`prompts/full.md:23,552-570`, now at `.claude/skills/mcp-server-stress/prompt.md` after initiatives 1+2) audits `skills/*/SKILL.md` against the live MCP catalog — verifies each shipped skill's tool references resolve. This is conceptually a static surface check, not a server-execution check. The existing `.claude/skills/surface-audit/` skill already owns the static-catalog audit lane. Keeping Phase 16b inside the server-stress skill is what coupled the skill to a Roslyn-MCP-repo checkout in the first place. |
| Approach | (a) Read the current Phase 16b content from `.claude/skills/mcp-server-stress/prompt.md`. (b) Edit `.claude/skills/surface-audit/SKILL.md` — absorb the Phase 16b workflow as a new "Skills audit" section between the existing static-catalog and doc-claim sections; preserve the `glob skills/*/SKILL.md` discovery pattern, frontmatter parity check, tool-reference resolution check, and pass/flag/fail tagging; relocate the MCP audit checkpoint at end of Phase 16b (`prompts/full.md:570`) into surface-audit's report section. (c) Edit `.claude/skills/mcp-server-stress/prompt.md` — delete Phase 16b section + every "plugin-skills audit" mention; update the report-format table at lines 757-784 of the original full.md to drop the skills-audit row. (d) Update `tests/RoslynMcp.Tests/Skills/McpServerStressSkillFrontmatterTests.cs` — assert SKILL.md no longer mentions Phase 16b or "plugin-skill audit". |
| Scope | **Production files: 2** — `.claude/skills/surface-audit/SKILL.md`, `.claude/skills/mcp-server-stress/prompt.md`. **Test files: 1** — `McpServerStressSkillFrontmatterTests.cs` (extend with the new negative assertion). |
| Tool policy | `edit-only` |
| Estimated context cost | 30000 |
| Risks | (a) Phase 16b's "discover live skills via `glob skills/*/SKILL.md`" must be preserved exactly — it's the source-of-truth discovery pattern that prevents drift from a hand-maintained list. (b) Don't merge before initiative 2 completes — this depends on the rename. |
| Validation | (1) `mcp__roslyn__compile_check` + `mcp__roslyn__test_run --filter "McpServerStressSkillFrontmatterTests"`. (2) `./eng/verify-ai-docs.ps1`. (3) Manual: `/surface-audit` output now includes a "Skills audit" section; `/mcp-server-stress` output no longer includes Phase 16b / plugin-skill rows. |
| Performance review | N/A — content move between skills. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Moved plugin-skill audit (formerly Phase 16b of `/audit-deep`) into `/surface-audit` where the static-catalog audit already lives. `/mcp-server-stress` now audits only the running server's surface; `/surface-audit` covers the static catalog plus shipped skills layered on it. |
| Backlog sync | Close rows: `extract-skills-audit-from-server-stress`. |

### 5. backlog-d-fragment-pattern

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `backlog-d-fragment-pattern` |
| Diagnosis | Today an audit run in repo X has to cross-write its `*_mcp-server-audit.md` into `<Roslyn-MCP-root>/ai_docs/audit-reports/` (`prompts/full.md:663-664`) or stage to `review-inbox/` and rely on the operator copying it later. Fragile, racy, often forgotten. The `changelog.d/` pattern at `.claude/skills/draft-changelog-entry/` and `.claude/skills/bump/` already proves the fragment-then-consolidate flow works for this repo. Apply it: audit run writes one fragment per actionable finding into the audited repo's local `backlog.d/`; `/backlog-intake` consolidates fragments from configured sibling repos and deletes consumed ones at the source. |
| Approach | (a) NEW `ai_docs/items/backlog-d-fragment-schema.md` — define the fragment file shape: frontmatter (`id`: stable hash for dedup, `source_audit`, `source_repo`, `severity`: P0-P3, `area`: tools/resources/prompts/skills/concurrency/perf, `anchors`: list of file:line) + body (one paragraph: finding + repro + proposed fix sketch). (b) Edit `.claude/skills/mcp-server-stress/prompt.md` — at end of Phase 18 (or new Phase 19), emit one fragment per actionable finding to `<audited-repo>/backlog.d/<finding-id>.md`; document that prose `.md` evidence and scorecard JSON stay where they are written; fragments are the only consumable. (c) Edit `.claude/skills/backlog-intake/SKILL.md` — walk configured sibling repos, collect `backlog.d/*.md` fragments, dedupe against existing rows in `<Roslyn-MCP-root>/ai_docs/backlog.md` by `id` then anchor-set similarity, append new rows, delete consumed fragments at source. (d) Edit `eng/stage-review-inbox.ps1` — extend discovery to include `backlog.d/*.md` from sibling repos alongside existing audit-report discovery; document fragment-deletion-after-consume. |
| Scope | **Production files: 4** — NEW `ai_docs/items/backlog-d-fragment-schema.md`, EDIT `.claude/skills/mcp-server-stress/prompt.md`, EDIT `.claude/skills/backlog-intake/SKILL.md`, EDIT `eng/stage-review-inbox.ps1`. **Test files: 0**. |
| Tool policy | `edit-only` |
| Estimated context cost | 55000 |
| Risks | (a) Fragment `id` collision across sibling repos — schema must include `source_repo` to disambiguate. (b) Idempotency: re-running intake on a clean `backlog.d/` must be a no-op; intake must dedupe by content hash, not just filename. (c) Fragment-deletion-at-source means intake mutates audited repos — intentional, but document explicitly. (d) `stage-review-inbox.ps1` discovery filter must check frontmatter to disambiguate audit-report `*.md` from fragment `*.md`. |
| Validation | (1) `./eng/verify-ai-docs.ps1` after schema doc add. (2) Synthetic test: hand-create 2 fragments in a test sibling repo, run `/backlog-intake`, confirm rows appear in `ai_docs/backlog.md` and fragments are deleted at source. (3) Re-run intake — no-op. (4) Hand-edit one fragment to be an obvious duplicate of an existing row → intake skips with a dedup-skip log line. (5) `./eng/verify-release.ps1 -Configuration Release`. |
| Performance review | N/A — file I/O during intake. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added `backlog.d/` fragment pattern for cross-repo audit findings, mirroring the existing `changelog.d/` pattern. `/mcp-server-stress` runs in audited repo X now write actionable findings as fragments at `<X>/backlog.d/<finding-id>.md`; `/backlog-intake` consolidates fragments from configured sibling repos into `ai_docs/backlog.md` and deletes consumed fragments. Replaces the prior cross-repo write-and-copy flow. |
| Backlog sync | Close rows: `backlog-d-fragment-pattern`. |

### 6. per-repo-promotion-scorecard

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `per-repo-promotion-scorecard` |
| Diagnosis | Today's `_latest-promotion-scorecard.json` lives at `<Roslyn-MCP-root>/ai_docs/audit-reports/_latest-promotion-scorecard.json` (`prompts/full.md:671`) — single file, last-write-wins across all audited workspaces. Promotion decisions reflect whichever workspace audited last; a tool that fails on one repo's edge case gets `keep-experimental` even if it works cleanly on three other workspaces. Move scorecards per-repo and require quorum evidence (≥2 workspaces with `promote`, no `keep-experimental` or `deprecate`) before flipping a tier. |
| Approach | (a) Edit `.claude/skills/mcp-server-stress/prompt.md` — change scorecard write path from `<Roslyn-MCP-root>/...` to `<audited-repo>/ai_docs/audit-reports/_latest-promotion-scorecard.json`. (b) NEW `eng/aggregate-promotion-scorecards.ps1` — gather all `_latest-promotion-scorecard.json` files from configured sibling repos; merge by `tool|resource|prompt name`; emit aggregated quorum verdict per entry: `promote: ready` (≥2 promote, 0 keep-experimental, 0 deprecate), `promote: blocked` (any keep-experimental or deprecate), `needs-more-evidence` (<2 promote). (c) Edit `.claude/skills/publish-preflight/SKILL.md` Step 8 — invoke `eng/aggregate-promotion-scorecards.ps1`; rewrite the staleness/missing branches to reason about per-repo scorecards. (d) Edit `.claude/skills/promote-tier/SKILL.md` — accept aggregated input format in addition to existing single-scorecard format; document the quorum requirement. |
| Scope | **Production files: 4** — EDIT `.claude/skills/mcp-server-stress/prompt.md`, NEW `eng/aggregate-promotion-scorecards.ps1`, EDIT `.claude/skills/publish-preflight/SKILL.md`, EDIT `.claude/skills/promote-tier/SKILL.md`. **Test files: 1** — NEW `tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs` covering: 3 mock scorecards (2 promote, 1 needs-more-evidence) → `promote: ready`; same with 1 promote + 1 keep-experimental → `promote: blocked`; missing scorecards from N-1 sibling repos → `needs-more-evidence`. |
| Tool policy | `edit-only` |
| Estimated context cost | 55000 |
| Risks | (a) Sibling-repo discovery list must be config-driven (not hard-coded) — use the same configured sibling-repo list `/backlog-intake` walks (per initiative 5). (b) Quorum rule (≥2 promote, 0 blockers) is opinionated; surface it in `promote-tier/SKILL.md` and let the maintainer override per-flip when a strong single-repo signal warrants. (c) Backward compat: detect existing single-file scorecards in old locations and flag ("scorecard at deprecated path: <path>; expected per-repo path") so the maintainer migrates. (d) `/publish-preflight` Step 8 currently treats missing scorecard as INFO (`publish-preflight/SKILL.md:109`); keep that semantics — quorum-not-met is INFO too. |
| Validation | (1) `mcp__roslyn__compile_check` + `mcp__roslyn__test_run --filter "AggregatePromotionScorecardsScriptTests"`. (2) `pwsh ./eng/aggregate-promotion-scorecards.ps1` against the test-fixture sibling-repo set. (3) `/publish-preflight` against this repo with the new aggregator → Step 8 reports per-repo source with quorum verdict. (4) `./eng/verify-release.ps1 -Configuration Release`. |
| Performance review | N/A — script I/O across small JSON files. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Promotion scorecards are now per-audited-repo (`<repo>/ai_docs/audit-reports/_latest-promotion-scorecard.json`) instead of a single last-write-wins file at `<Roslyn-MCP-root>/...`. `/publish-preflight` Step 8 aggregates scorecards from configured sibling repos and applies a quorum rule (≥2 workspaces with `promote`, no blockers) before recommending a tier flip. Single-workspace anomalies no longer drive tier decisions. |
| Backlog sync | Close rows: `per-repo-promotion-scorecard`. |

## Self-vet checklist

The planner walked Step 7 before writing this revision. In prose (no literal link syntax in this checklist body):

- One row = one initiative for 4 of 5 backlog rows. The `audit-deep-relocate-and-rename` row maps to two initiatives (2 and 3) by mechanical necessity — the directory move + content edit naturally splits into "move + name" and "external references". Initiative 3 carries the row closure; initiative 2 has empty `backlogRowsClosed`. This is not a Rule-1 bundling violation (the OPPOSITE direction — one row across two initiatives is not bundling).
- All initiatives within Rule 3 (≤4 production files) **strictly**, no exemptions invoked. Initiative 1: 4. Initiative 2: 3. Initiative 3: 3. Initiative 4: 2. Initiative 5: 4. Initiative 6: 4.
- All initiatives within Rule 4 (≤3 test files). Initiative 1: 1. Initiative 2: 3. Initiative 3: 0. Initiative 4: 1. Initiative 5: 0. Initiative 6: 1.
- All `estimatedContextTokens` under 80K. Largest is 55K (initiatives 5 and 6).
- Every initiative has `toolPolicy: "edit-only"`. None of these touch C# source via `*_apply` tools.
- Cross-cutting fanout probed for initiative 1 (4 production touches match scope). For initiatives 2-6, fanout matches `productionFilesTouched` directly (small, scoped edits to skill/prompt files).
- No two adjacent-`order` initiatives both touch addenda-listed C# hotspots — this entire sweep edits `.claude/skills/`, `ai_docs/`, `eng/`, and tests; the C# hotspots (`ServerSurfaceCatalog.cs`, `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`) are untouched.
- No literal markdown bracket-paren link syntax in this checklist body (described in prose only, per the planner-discipline rule).
