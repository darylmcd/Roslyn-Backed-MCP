# Backlog sweep plan — 20260513T010000Z

**Generated:** 2026-05-13T01:00:00Z  
**Backlog snapshot:** 2026-05-12T23:44:39Z  
**Initiatives:** 10 (waves 2–11 of `skill-namespace-installed-as-bulk-frontmatter-migration`)  
**Status:** deepening complete — all 10/10 ok

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

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-7` |
| Diagnosis | All four target `skills/*/SKILL.md` files exist and none currently carry an `installed_as` frontmatter key. Grep across the entire `skills/` tree confirms zero `installed_as` occurrences, consistent with waves 5 and 6 being the next predecessors in the series. Wave 1 (PR #710) established the pattern: `installed_as: roslyn-mcp:<bare-name>` immediately after the `name:` line in the YAML frontmatter block (confirmed via `.claude/skills/backlog-intake/SKILL.md:3`). The four target files — `skills/format-sweep/SKILL.md`, `skills/generate-tests/SKILL.md`, `skills/impact-assessment/SKILL.md`, `skills/inheritance-explorer/SKILL.md` — all open with a standard `---` / `name:` / `description:` frontmatter block. No other structural issues found. |
| Approach | Edit each of the four SKILL.md files to insert `installed_as: roslyn-mcp:<bare-name>` as the second line of the YAML frontmatter block, immediately after `name:`. Exact values: `roslyn-mcp:format-sweep`, `roslyn-mcp:generate-tests`, `roslyn-mcp:impact-assessment`, `roslyn-mcp:inheritance-explorer`. Mirror the pattern established at `.claude/skills/backlog-intake/SKILL.md:3`. No other content in the files changes. After editing, run `pwsh -NoProfile -File eng/list-skills.ps1` to confirm the missing count drops by 4 from the pre-edit baseline. |
| Scope | Production files touched: 4 — `skills/format-sweep/SKILL.md`, `skills/generate-tests/SKILL.md`, `skills/impact-assessment/SKILL.md`, `skills/inheritance-explorer/SKILL.md`. Test files added/modified: 0. No files deleted. These are SKILL.md doc/config files; no C# production source is changed. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | Predecessor waves 5 and 6 must have shipped before this wave runs — the missing-count validation (22 → 18) only passes after both earlier waves are merged. Executor should verify the starting count before committing. Misspelling a value (e.g. `roslyn-mcp:format_sweep` with underscore) would cause `SkillFrontmatterInstalledAsTests` to fail once un-Ignored in wave 12 — use exact kebab-case. Fanout probe skipped — surgical frontmatter edit, no cross-cutting symbol changes. |
| Validation | 1. Run `pwsh -NoProfile -File eng/list-skills.ps1` before editing and confirm the `[missing]` count baseline. 2. Apply the four frontmatter edits. 3. Run `pwsh -NoProfile -File eng/list-skills.ps1` again and confirm `[missing]` count dropped by 4. 4. Run `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` to confirm no build regressions. 5. Run `./eng/verify-ai-docs.ps1` to confirm doc-link checks pass. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:<bare-name>` frontmatter to `skills/format-sweep`, `skills/generate-tests`, `skills/impact-assessment`, and `skills/inheritance-explorer` SKILL.md files (wave 7 of 12 in the bulk `installed_as` migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-7`]. |

---

### 7. skill-namespace-installed-as-wave-8

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-8` |
| Diagnosis | All four anchor SKILL.md files exist and confirmed missing `installed_as:` frontmatter. Verified by reading each file directly: `skills/mcp-server-surface-test/SKILL.md` (line 2: `name: mcp-server-surface-test`, no `installed_as:` present), `skills/migrate-package/SKILL.md` (line 2: `name: migrate-package`, no `installed_as:` present), `skills/modernize/SKILL.md` (line 2: `name: modernize`, no `installed_as:` present), `skills/nuget-preflight/SKILL.md` (line 2: `name: nuget-preflight`, no `installed_as:` present). A repo-wide grep for `installed_as` in `skills/*/SKILL.md` returned zero matches — the entire `skills/` tree remains unpatched. These four files are shipped plugin skills so the correct namespace prefix is `roslyn-mcp:`. |
| Approach | For each of the four SKILL.md files, insert `installed_as: roslyn-mcp:<bare-name>` as a new line immediately after the `name:` line in the YAML frontmatter block (between current line 2 and line 3), shifting `description:` down by one. Mirror the exact insertion pattern established in PR #710 wave 1 and carried through waves 2–7. Specific values: `skills/mcp-server-surface-test/SKILL.md` — insert `installed_as: roslyn-mcp:mcp-server-surface-test`; `skills/migrate-package/SKILL.md` — insert `installed_as: roslyn-mcp:migrate-package`; `skills/modernize/SKILL.md` — insert `installed_as: roslyn-mcp:modernize`; `skills/nuget-preflight/SKILL.md` — insert `installed_as: roslyn-mcp:nuget-preflight`. No other content in any file changes. |
| Scope | Production files touched: 4 — `skills/mcp-server-surface-test/SKILL.md`, `skills/migrate-package/SKILL.md`, `skills/modernize/SKILL.md`, `skills/nuget-preflight/SKILL.md`. Test files added/modified: 0. Files deleted: 0. All four files are YAML-frontmatter-only edits; no C# source or test files are involved. Rule 3 cap (≤ 4 production files) satisfied exactly. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) The `installed_as:` value must match `^roslyn-mcp:[a-z][a-z0-9-]+$` exactly — a typo or wrong prefix will cause `SkillFrontmatterInstalledAsTests` to fail when wave 12 activates it. (2) The absolute missing-count target (18 → 14) assumes waves 4–7 have all landed; if any prior wave is not yet merged, executor should validate by delta (−4 missing) rather than absolute count. (3) `eng/list-skills.ps1` parses frontmatter with a simple colon-split regex — confirm the inserted line uses plain unquoted `installed_as: roslyn-mcp:bare-name`. (4) Fanout probe skipped — surgical doc-only edit, no cross-cutting symbol changes. |
| Validation | 1. Run `pwsh -NoProfile -File eng/list-skills.ps1` before the edits; record the current missing count. 2. Make all four edits. 3. Re-run `pwsh -NoProfile -File eng/list-skills.ps1`; confirm missing count decreased by exactly 4. 4. Confirm each of the four edited files contains exactly one `installed_as: roslyn-mcp:<bare-name>` line immediately after `name:` in the frontmatter block. 5. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` to confirm doc-check passes. 6. No dotnet build or test run required for this doc-only change. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:<name>` frontmatter to `skills/mcp-server-surface-test`, `skills/migrate-package`, `skills/modernize`, and `skills/nuget-preflight` SKILL.md files (wave 8 of 12 bulk frontmatter migration, reducing missing count from 18 to 14). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-8`]. |

---

### 8. skill-namespace-installed-as-wave-9

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-9` |
| Diagnosis | All four anchor SKILL.md files are confirmed present and confirmed missing `installed_as:` frontmatter. Direct reads verify: `skills/project-inspection/SKILL.md` line 2 is `name: project-inspection` with no `installed_as:`; `skills/refactor-loop/SKILL.md` line 2 is `name: refactor-loop` with no `installed_as:`; `skills/refactor/SKILL.md` line 2 is `name: refactor` with no `installed_as:`; `skills/review/SKILL.md` line 2 is `name: review` with no `installed_as:`. A repo-wide Grep for `installed_as` across `skills/*/SKILL.md` returned zero matches, confirming no wave has already touched these files. The backlog row's stated missing-count drop of 14 → 10 is correct for this wave's 4 files. |
| Approach | For each of the four SKILL.md files, insert `installed_as: roslyn-mcp:<bare-name>` immediately after the `name:` line in the YAML frontmatter block, shifting `description:` down by one line. No other content in any file changes. Specific edits: (1) `skills/project-inspection/SKILL.md` line 3: insert `installed_as: roslyn-mcp:project-inspection`; (2) `skills/refactor-loop/SKILL.md` line 3: insert `installed_as: roslyn-mcp:refactor-loop`; (3) `skills/refactor/SKILL.md` line 3: insert `installed_as: roslyn-mcp:refactor`; (4) `skills/review/SKILL.md` line 3: insert `installed_as: roslyn-mcp:review`. After all four edits, validate with `pwsh -NoProfile -File eng/list-skills.ps1` — missing count must drop by exactly 4 from the pre-edit baseline. |
| Scope | Production files touched: 4 — `skills/project-inspection/SKILL.md`, `skills/refactor-loop/SKILL.md`, `skills/refactor/SKILL.md`, `skills/review/SKILL.md`. Test files added/modified: 0. Files deleted: 0. All edits are YAML-frontmatter-only insertions; no logic, no C# changes. Rule 3: 4 files at the hard cap. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) All four values must use the `roslyn-mcp:` namespace prefix (shipped plugin skills under `skills/`) — do not use bare names as used in `.claude/skills/`. (2) The `SkillFrontmatterInstalledAsTests` CI test remains `[Ignore]`-marked until wave 12 completes; a typo in an `installed_as` value will not be caught by CI until then. (3) Wave ordering: if waves 2–8 have not all shipped when this wave executes, validate using delta (−4 missing from pre-edit baseline) rather than absolute target of 10. (4) Fanout probe skipped — surgical doc-only frontmatter edit, no cross-cutting symbol changes. |
| Validation | 1. Record the missing count from `pwsh -NoProfile -File eng/list-skills.ps1` before the edit. 2. Make all four edits. 3. Re-run `eng/list-skills.ps1`; confirm missing count decreased by exactly 4. 4. Visually confirm each edited file's frontmatter block follows the shape: `---`, `name: <bare-name>`, `installed_as: roslyn-mcp:<bare-name>`, `description: ...`, remaining fields, `---`. 5. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` (doc-link check passes). 6. No C# compile check required — zero C# files changed. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:*` frontmatter to `skills/project-inspection`, `skills/refactor-loop`, `skills/refactor`, and `skills/review` SKILL.md files (wave 9 of 12 in the bulk `installed_as` migration), reducing the missing-field count from 14 to 10. |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-9`]. |

---

### 9. skill-namespace-installed-as-wave-10

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-10` |
| Diagnosis | All four target SKILL.md files exist and are missing the `installed_as:` frontmatter field, confirmed by live inspection. `skills/security/SKILL.md` line 1–6 frontmatter has `name: security` with no `installed_as` key. `skills/semantic-find/SKILL.md` line 1–6 has `name: semantic-find` with no `installed_as` key. `skills/session-undo/SKILL.md` line 1–6 has `name: session-undo` with no `installed_as` key. `skills/snippet-eval/SKILL.md` line 1–6 has `name: snippet-eval` with no `installed_as` key. The backlog description is fully accurate — these are shipped plugin skills requiring the `roslyn-mcp:` namespace prefix per the pattern established in PR #710. |
| Approach | Edit the YAML frontmatter of each of the four files. In each file, insert `installed_as: roslyn-mcp:<bare-name>` immediately after the `name:` line (line 2 of the frontmatter block). Specific edits: (1) `skills/security/SKILL.md` — add `installed_as: roslyn-mcp:security` after `name: security`. (2) `skills/semantic-find/SKILL.md` — add `installed_as: roslyn-mcp:semantic-find` after `name: semantic-find`. (3) `skills/session-undo/SKILL.md` — add `installed_as: roslyn-mcp:session-undo` after `name: session-undo`. (4) `skills/snippet-eval/SKILL.md` — add `installed_as: roslyn-mcp:snippet-eval` after `name: snippet-eval`. No code changes. No test changes. |
| Scope | Production files touched: 4 — `skills/security/SKILL.md`, `skills/semantic-find/SKILL.md`, `skills/session-undo/SKILL.md`, `skills/snippet-eval/SKILL.md`. Test files added/modified: 0. No Rule 3 exemption needed — 4 files exactly at the cap. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | Low risk — pure YAML frontmatter addition with no behavioral impact. Adjacent behavior to verify: `eng/list-skills.ps1` output should show the 4 new `installed_as` values (not `[missing]`) and the missing count should drop by 4. The `[Ignore]`-marked `SkillFrontmatterInstalledAsTests.cs` test will not run; that guard is intentional until wave 12. Fanout probe skipped — surgical frontmatter edits, no cross-cutting symbol changes. |
| Validation | 1. Run `pwsh -NoProfile -File eng/list-skills.ps1` from repo root — confirm `installed_as` shows `roslyn-mcp:security`, `roslyn-mcp:semantic-find`, `roslyn-mcp:session-undo`, `roslyn-mcp:snippet-eval` for the four updated files; confirm missing count drops by 4. 2. Run `./eng/verify-ai-docs.ps1` to confirm no doc-link regressions. 3. Optionally run `dotnet build RoslynMcp.slnx -c Release` to confirm no compile impact (no source changes). |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:<name>` frontmatter to `skills/security`, `skills/semantic-find`, `skills/session-undo`, and `skills/snippet-eval` SKILL.md files (wave 10/12 of bulk frontmatter migration). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-10`]. |

---

### 10. skill-namespace-installed-as-wave-11

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `skill-namespace-installed-as-wave-11` |
| Diagnosis | All four anchor SKILL.md files confirmed present in the current tree and all confirmed missing `installed_as:` frontmatter. Direct reads verify: `skills/test-coverage/SKILL.md` (line 2: `name: test-coverage`, no `installed_as:`); `skills/test-triage/SKILL.md` (line 2: `name: test-triage`, no `installed_as:`); `skills/trace-flow/SKILL.md` (line 2: `name: trace-flow`, no `installed_as:`); `skills/update/SKILL.md` (line 2: `name: update`, no `installed_as:`). All four are in the `skills/` tree (shipped plugin skills), so each value must carry the `roslyn-mcp:` namespace prefix per the established wave convention. Note: `skills/update/SKILL.md` is the shipped plugin skill distinct from `.claude/skills/update/SKILL.md` (bare name `update`, already patched in wave 4) — executor must not confuse the two paths. By the time wave 11 executes, waves 2–10 will have resolved 36 of the original 42 missing entries, leaving exactly 6 — consistent with the backlog row's stated precondition. |
| Approach | For each of the four `skills/` SKILL.md files, insert `installed_as: roslyn-mcp:<bare-name>` as a new line immediately after the `name:` line in the YAML frontmatter block, shifting `description:` and subsequent lines down by one. Mirror the exact insertion pattern from `skills/backlog-intake/SKILL.md:3` (wave 1 reference). Specific edits: (1) `skills/test-coverage/SKILL.md` — insert `installed_as: roslyn-mcp:test-coverage` after line 2. (2) `skills/test-triage/SKILL.md` — insert `installed_as: roslyn-mcp:test-triage` after line 2. (3) `skills/trace-flow/SKILL.md` — insert `installed_as: roslyn-mcp:trace-flow` after line 2. (4) `skills/update/SKILL.md` — insert `installed_as: roslyn-mcp:update` after line 2. No other content in any file changes. |
| Scope | Production files touched: 4 — `skills/test-coverage/SKILL.md`, `skills/test-triage/SKILL.md`, `skills/trace-flow/SKILL.md`, `skills/update/SKILL.md`. Test files added: 0 (the `SkillFrontmatterInstalledAsTests.cs` un-Ignore step is deferred to wave 12). Files deleted: 0. Doc-only frontmatter edit — no C# source files touched. Count is exactly 4, at the Rule 3 hard ceiling. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) `skills/update/SKILL.md` and `.claude/skills/update/SKILL.md` are two distinct files — the former needs `roslyn-mcp:update` (shipped plugin, `skills/` tree), while the latter already received bare `update` in wave 4. Executor must verify the file path before editing to avoid writing the wrong value to the wrong file. (2) The `installed_as:` value must match `^roslyn-mcp:[a-z][a-z0-9-]+$` exactly — a typo or missing prefix will cause the `SkillFrontmatterInstalledAsTests` lock-step test (currently `[Ignore]`-marked) to fail when wave 12 activates it. (3) Wave ordering: if validating by absolute count, all prior waves (2–10) must have shipped for the missing total to read 6 → 2; executor should use delta (−4) if any prior wave is still pending. (4) Fanout probe skipped — surgical doc-only YAML frontmatter insertion, no cross-cutting symbol changes. |
| Validation | 1. Before edits, run `pwsh -NoProfile -File eng/list-skills.ps1` and record the current missing count as baseline. 2. Make all four edits. 3. Re-run `eng/list-skills.ps1` — confirm missing count decreased by exactly 4; if all prior waves shipped, absolute missing count should read 2. 4. Confirm each edited file contains exactly one `installed_as:` line immediately after `name:` in the frontmatter block, with the value matching `^roslyn-mcp:[a-z][a-z0-9-]+$`. 5. Run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` to confirm doc-link checker passes. 6. No dotnet build or test run required for this doc-only change. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as: roslyn-mcp:<name>` frontmatter to `skills/test-coverage`, `skills/test-triage`, `skills/trace-flow`, and `skills/update` SKILL.md files (wave 11/12 of bulk `installed_as` migration), reducing the missing-field count from 6 to 2. |
| Backlog sync | Close rows: [`skill-namespace-installed-as-wave-11`]. |
