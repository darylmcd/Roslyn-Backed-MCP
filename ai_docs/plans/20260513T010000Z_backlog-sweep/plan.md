# Backlog sweep plan — 20260513T010000Z

**Generated:** 2026-05-13T01:00:00Z  
**Backlog snapshot:** 2026-05-12T23:44:39Z  
**Initiatives:** 10 (waves 2–11 of `skill-namespace-installed-as-bulk-frontmatter-migration`)  
**Status:** deepening complete (batch 1 merged; batch 2 pending)

---

## Selection rationale

All 8 Reserved rows (good-first-issue) skipped per sweep rules. `tool-surface-pagination-or-tool-sets` skipped (weaker evidence, speculative). `workspace-manager-cache-store-extraction` and remaining Defer rows excluded. Master row `skill-namespace-installed-as-bulk-frontmatter-migration` excluded (meta-description; work is in spin-off rows). Wave 12 excluded: its test-activation step (`remove [Ignore]` from `SkillFrontmatterInstalledAsTests.cs`) depends on waves 2–11 completing; deferred to next sweep.

---

### 1. skill-namespace-installed-as-wave-2

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-2` |
| Diagnosis | Confirmed: none of the four target SKILL.md files carry `installed_as:` in their frontmatter. Running `pwsh -NoProfile -File eng/list-skills.ps1` against the current tree reports 46 skills, 42 missing `installed_as:` (shown in yellow). All four files exist at their cited paths: `.claude/skills/draft-changelog-entry/SKILL.md` (line 2 is `name: draft-changelog-entry`), `.claude/skills/mcp-server-stress/SKILL.md` (line 2 is `name: mcp-server-stress`), `.claude/skills/promote-tier/SKILL.md` (line 2 is `name: promote-tier`), `.claude/skills/publish-preflight/SKILL.md` (line 2 is `name: publish-preflight`). Wave-1 established the canonical frontmatter shape — `installed_as: <bare-name>` inserted on line 3, immediately after `name:` (confirmed in `.claude/skills/backlog-intake/SKILL.md:3`). The missing count will drop from 42 to 38 upon completion. |
| Approach | For each of the four SKILL.md files, insert `installed_as: <bare-name>` as a new line immediately after the `name:` line (line 2 → new line 3), shifting the existing `description:` line down by one. Mirror the exact pattern established in `.claude/skills/backlog-intake/SKILL.md:3`. Values: `draft-changelog-entry` → `installed_as: draft-changelog-entry`; `mcp-server-stress` → `installed_as: mcp-server-stress`; `promote-tier` → `installed_as: promote-tier`; `publish-preflight` → `installed_as: publish-preflight`. No other content in any file changes. After edits, validate with `pwsh -NoProfile -File eng/list-skills.ps1` — missing count must read 38. |
| Scope | Production files touched: 4 — `.claude/skills/draft-changelog-entry/SKILL.md`, `.claude/skills/mcp-server-stress/SKILL.md`, `.claude/skills/promote-tier/SKILL.md`, `.claude/skills/publish-preflight/SKILL.md`. Test files added: 0. Files deleted: 0. Note: these are maintainer-only SKILL.md files; counted as production files under Rule 3. Count is exactly 4, at the Rule 3 hard ceiling. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | All four edits are YAML-frontmatter-only insertions with no logic change. `eng/list-skills.ps1` parses frontmatter with a simple colon-split regex — confirm the inserted line uses plain `installed_as: bare-name` with no quotes (the parser strips double-quotes but the wave-1 convention is unquoted bare names). No test files exist for these SKILL.md files, so the only validation is the list-skills.ps1 count check. Fanout probe skipped — surgical doc-only edit, no cross-cutting symbol changes. |
| Validation | 1. After edits, run `pwsh -NoProfile -File eng/list-skills.ps1` — output must show `installed_as` populated for all four files (not `[missing]`) and trailing summary must read `42 skill(s) found. 38 missing installed_as:`. 2. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` to confirm doc-check passes. 3. Visually confirm each edited file's frontmatter block: `---`, `name: <bare-name>`, `installed_as: <bare-name>`, `description: ...`, remaining fields, `---`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Added `installed_as:` frontmatter field to 4 maintainer-only `.claude/skills/` SKILL.md files (`draft-changelog-entry`, `mcp-server-stress`, `promote-tier`, `publish-preflight`), reducing the missing-field count from 42 to 38. |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-2`]. |

---

### 2. skill-namespace-installed-as-wave-3

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-3` |
| Diagnosis | All four target SKILL.md files exist and are confirmed missing `installed_as:` frontmatter. Verified by reading each file: `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md` (line 2: `name: reconcile-backlog-sweep-plan`), `.claude/skills/reconcile-backlog-vs-issues/SKILL.md` (line 2: `name: reconcile-backlog-vs-issues`), `.claude/skills/recover-stalled-subagent/SKILL.md` (line 2: `name: recover-stalled-subagent`), `.claude/skills/release-cut/SKILL.md` (line 2: `name: release-cut`). None contain `installed_as:` anywhere in their frontmatter block. The backlog row's stated drop of 38→34 counts across the full 46-file set (including `skills/` files still untouched by waves 2–3), which is consistent. |
| Approach | Insert `installed_as: <bare-name>` immediately after the `name:` line in each of the four frontmatter blocks. No namespace prefix — `.claude/skills/` files are maintainer-only, not shipped plugin skills. Specific edits: (1) `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md` line 3: insert `installed_as: reconcile-backlog-sweep-plan`. (2) `.claude/skills/reconcile-backlog-vs-issues/SKILL.md` line 3: insert `installed_as: reconcile-backlog-vs-issues`. (3) `.claude/skills/recover-stalled-subagent/SKILL.md` line 3: insert `installed_as: recover-stalled-subagent`. (4) `.claude/skills/release-cut/SKILL.md` line 3: insert `installed_as: release-cut`. The `SkillFrontmatterInstalledAsTests` CI test remains `[Ignore]`-marked until wave 12 — no test file changes in this wave. |
| Scope | Production files touched: 4 — `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`, `.claude/skills/reconcile-backlog-vs-issues/SKILL.md`, `.claude/skills/recover-stalled-subagent/SKILL.md`, `.claude/skills/release-cut/SKILL.md`. Test files added/modified: 0. Files deleted: 0. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | (1) Inserting on the wrong line (e.g. before `name:` instead of after) would produce invalid frontmatter ordering — executor must verify the `name:` line is line 2 before inserting. (2) Wave 2 must ship before wave 3 to keep the missing-count validation in sync with the backlog row's stated pre-condition of 38 missing; if run out of order the count drop assertion still holds (4 fewer), but the absolute number will differ. (3) The `SkillFrontmatterInstalledAsTests` test is `[Ignore]`-marked — CI will not catch a typo until wave 12. Executor should run `pwsh -NoProfile -File eng/list-skills.ps1` post-edit to confirm the missing count drops by 4. |
| Validation | 1. After edits, run `pwsh -NoProfile -File eng/list-skills.ps1` and confirm the four edited skills no longer appear in the missing list. 2. Confirm missing count drops by 4 from the pre-edit value. 3. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` (fast doc-link check). 4. Build check: `mcp__roslyn__compile_check` — no C# changes, confirms no inadvertent file corruption. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as` frontmatter to `.claude/skills/reconcile-backlog-sweep-plan`, `reconcile-backlog-vs-issues`, `recover-stalled-subagent`, and `release-cut` SKILL.md files (wave 3 of bulk migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-3`]. |

---

### 3. skill-namespace-installed-as-wave-4

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-4` |
| Diagnosis | All four cited anchor files are confirmed present and all currently show `installed_as: [missing]` per `eng/list-skills.ps1` live output. The two `.claude/skills/` files need bare-name values (no namespace — maintainer-only skills); the two `skills/` files need `roslyn-mcp:` prefixed values (shipped plugin skills). Live state confirmed: `.claude/skills/surface-audit/SKILL.md` frontmatter has `name: surface-audit` with no `installed_as:`; `.claude/skills/update/SKILL.md` has `name: update` with no `installed_as:`; `skills/analyze/SKILL.md` has `name: analyze` with no `installed_as:`; `skills/architecture-review/SKILL.md` has `name: architecture-review` with no `installed_as:`. Note: `eng/list-skills.ps1` currently reports 42 missing (not 34 as the backlog row predicts) because waves 2 and 3 have not yet executed. The wave 4 precondition count of 34 → 30 is correct only after waves 2 and 3 land; executor should validate using delta (−4 missing) rather than absolute count. |
| Approach | Edit all four SKILL.md files, inserting the `installed_as:` line immediately after `name:` in the frontmatter block: (1) `.claude/skills/surface-audit/SKILL.md` — insert `installed_as: surface-audit`; (2) `.claude/skills/update/SKILL.md` — insert `installed_as: update`; (3) `skills/analyze/SKILL.md` — insert `installed_as: roslyn-mcp:analyze`; (4) `skills/architecture-review/SKILL.md` — insert `installed_as: roslyn-mcp:architecture-review`. After all four edits, run `pwsh -NoProfile -File eng/list-skills.ps1` and verify the missing count dropped by exactly 4 from the pre-edit baseline. The `skills/` namespace values must match `^roslyn-mcp:[a-z][a-z0-9-]+$`; the `.claude/skills/` values must match `^[a-z][a-z0-9-]+$`. |
| Scope | Production files: 4 — `.claude/skills/surface-audit/SKILL.md`, `.claude/skills/update/SKILL.md`, `skills/analyze/SKILL.md`, `skills/architecture-review/SKILL.md`. Test files: 0. No deletions. Rule 3: 4 production files at the hard cap; all are doc-only frontmatter edits. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | (1) `.claude/skills/update/SKILL.md` is a maintainer override of the shipped `skills/update/SKILL.md` — the bare value `update` is correct for the `.claude/` tree; executor must not accidentally set `roslyn-mcp:update` on this file. (2) Wave ordering: executor should validate delta (−4 missing) rather than absolute target (30) if waves 2/3 have not yet shipped. (3) `eng/verify-skills-are-generic.ps1` may scan `.claude/skills/update/SKILL.md` — the new `installed_as:` line is frontmatter, not body, so it should not trigger the generic-skill check, but executor should confirm. |
| Validation | 1. Run `pwsh -NoProfile -File eng/list-skills.ps1` before the edit; record the missing count. 2. Make all four edits. 3. Re-run `eng/list-skills.ps1`; confirm missing count decreased by exactly 4. 4. Confirm the four `installed_as:` values match their expected patterns. 5. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1`. 6. No test run required for this wave. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as:` frontmatter to `.claude/skills/surface-audit`, `.claude/skills/update`, `skills/analyze`, and `skills/architecture-review` SKILL.md files (wave 4 of 12 in the bulk `installed_as` migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-4`]. |

---

### 4. skill-namespace-installed-as-wave-5

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-5` |
| Diagnosis | All four anchor SKILL.md files exist and confirmed present in the current tree. Frontmatter inspection shows each file has `name:` but no `installed_as:` key: `skills/code-actions/SKILL.md` line 2 (`name: code-actions`), `skills/complexity/SKILL.md` line 2 (`name: complexity`), `skills/dead-code/SKILL.md` line 2 (`name: dead-code`), `skills/di-audit/SKILL.md` line 2 (`name: di-audit`). The fix is a mechanical one-line frontmatter insert in each file. Matches the backlog description exactly; no staleness detected. |
| Approach | For each of the four SKILL.md files, insert `installed_as: roslyn-mcp:<bare-name>` as a new line immediately after the `name:` line in the YAML frontmatter block. Specific values: `roslyn-mcp:code-actions` in `skills/code-actions/SKILL.md`, `roslyn-mcp:complexity` in `skills/complexity/SKILL.md`, `roslyn-mcp:dead-code` in `skills/dead-code/SKILL.md`, `roslyn-mcp:di-audit` in `skills/di-audit/SKILL.md`. Mirror the exact insertion pattern from PR #710 wave 1. No other file changes needed; no source code touched. |
| Scope | Production files touched: 4 — `skills/code-actions/SKILL.md`, `skills/complexity/SKILL.md`, `skills/dead-code/SKILL.md`, `skills/di-audit/SKILL.md`. Test files added: 0 (the `SkillFrontmatterInstalledAsTests.cs` un-Ignore step is deferred to wave 12). Doc-only edit — no C# source files touched. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | The `installed_as:` value must match the regex `^roslyn-mcp:[a-z][a-z0-9-]+$` exactly; a typo or wrong prefix will cause the `SkillFrontmatterInstalledAsTests` lock-step test (still `[Ignore]`-marked) to fail when wave 12 activates it. Run `eng/list-skills.ps1` after the edit to confirm the missing count drops by 4. Waves 4 and 5 both touch `skills/` files but do not share any file, so parallel execution is safe. |
| Validation | 1. Run `pwsh -NoProfile -File eng/list-skills.ps1` from repo root — confirm missing `installed_as:` count drops by 4. 2. Confirm each of the four files contains exactly one `installed_as:` line immediately after `name:` in the frontmatter. 3. Run `./eng/verify-ai-docs.ps1` to confirm no doc-link or schema errors introduced. 4. Build check: `mcp__roslyn__compile_check` (formality; expected zero new diagnostics). |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:<name>` frontmatter to `skills/code-actions`, `skills/complexity`, `skills/dead-code`, and `skills/di-audit` SKILL.md files (wave 5 of 12 bulk migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-5`]. |

---

### 5. skill-namespace-installed-as-wave-6

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-6` |
| Diagnosis | All four target SKILL.md files confirmed present in the current tree and all confirmed missing `installed_as` frontmatter (grep for `installed_as` across `skills/*/SKILL.md` returns zero matches repo-wide). The fix is a pure YAML frontmatter insertion — one line per file, no prose or logic change. Pattern: insert `installed_as: roslyn-mcp:<bare-name>` immediately after the `name:` line in the YAML frontmatter block of each file, consistent with the `roslyn-mcp:` namespace used for shipped plugin skills. |
| Approach | Edit four files — add `installed_as: roslyn-mcp:document` after `name: document` in `skills/document/SKILL.md`; add `installed_as: roslyn-mcp:exception-audit` after `name: exception-audit` in `skills/exception-audit/SKILL.md`; add `installed_as: roslyn-mcp:explain-error` after `name: explain-error` in `skills/explain-error/SKILL.md`; add `installed_as: roslyn-mcp:extract-method` after `name: extract-method` in `skills/extract-method/SKILL.md`. The `installed_as:` key sits on the line immediately after `name:` inside the `---` YAML block. No other prose, logic, or test file changes. |
| Scope | Production files touched: 4 — `skills/document/SKILL.md`, `skills/exception-audit/SKILL.md`, `skills/explain-error/SKILL.md`, `skills/extract-method/SKILL.md`. Test files added: 0 (CI enforcement test `SkillFrontmatterInstalledAsTests` remains `[Ignore]`-marked until wave 12). Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | Adjacent behavior: `eng/list-skills.ps1` reads frontmatter at runtime — verify the key name casing matches what the script expects (`installed_as:` lowercase, no quotes around the value). If any SKILL.md file has non-standard YAML frontmatter delimiter or unexpected whitespace around the `name:` line, the insertion position may shift; executor should read each file before editing. |
| Validation | Run `pwsh -NoProfile -File eng/list-skills.ps1` and confirm missing `installed_as` count drops by 4. Spot-check each edited file's YAML frontmatter parses cleanly (valid YAML: no duplicate keys, correct indentation). Run `./eng/verify-ai-docs.ps1` to confirm doc-link checker passes. No dotnet build or test run required for this doc-only change. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as` frontmatter to `skills/document`, `skills/exception-audit`, `skills/explain-error`, and `skills/extract-method` SKILL.md files (wave 6/12 of bulk frontmatter migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-6`]. |

---

### 6. skill-namespace-installed-as-wave-7

<!-- deepener placeholder -->

---

### 7. skill-namespace-installed-as-wave-8

<!-- deepener placeholder -->

---

### 8. skill-namespace-installed-as-wave-9

<!-- deepener placeholder -->

---

### 9. skill-namespace-installed-as-wave-10

<!-- deepener placeholder -->

---

### 10. skill-namespace-installed-as-wave-11

<!-- deepener placeholder -->
