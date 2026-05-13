# Backlog sweep plan — 20260513T140000Z

**Generated:** 2026-05-13T14:00:00Z
**Backlog snapshot:** 2026-05-13T13:58:46Z
**Initiative count:** 1
**Anchor verification:** performed

## Selection notes

Eligible rows (count=10 requested; 1 found):

- All Critical, High, and Medium bands are empty.
- Low rows: 7 of 10 carry `Reserved — (good first issue)` markers and are hard-skipped per backlog standing rules. `tool-surface-pagination-or-tool-sets` is functionally deferred ("track only; act when..."). `skill-namespace-installed-as-bulk-frontmatter-migration` is a tracking meta-row closed by wave-12 in the same PR.
- `skill-namespace-installed-as-wave-12` is the only claimable row.

## Initiatives (in order)

### 1. skill-namespace-installed-as-wave-12

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | skill-namespace-installed-as-wave-12, skill-namespace-installed-as-bulk-frontmatter-migration |
| Diagnosis | Wave 12/12 (final wave) of the bulk `installed_as:` frontmatter migration. Confirmed: `skills/version-bump/SKILL.md` frontmatter (lines 1-6) has no `installed_as:` key. Confirmed: `skills/workspace-health/SKILL.md` frontmatter (lines 1-6) has no `installed_as:` key. Confirmed: `tests/RoslynMcp.Tests/Skills/SkillFrontmatterInstalledAsTests.cs:39` carries `[Ignore("Pending bulk frontmatter migration…")]` suppressing CI enforcement. All previous waves 2–11 shipped in PRs #716–#726. Master tracking row `skill-namespace-installed-as-bulk-frontmatter-migration` is still open; its row description explicitly requires closure in the same PR as wave-12 — this is a tracking-row closure, not a Rule 1 bundle of two independent implementations. |
| Approach | (1) Add `installed_as: roslyn-mcp:version-bump` to `skills/version-bump/SKILL.md` frontmatter after the `argument-hint:` line. (2) Add `installed_as: roslyn-mcp:workspace-health` to `skills/workspace-health/SKILL.md` frontmatter after the `argument-hint:` line. (3) Remove the `[Ignore]` attribute at line 39 of `tests/RoslynMcp.Tests/Skills/SkillFrontmatterInstalledAsTests.cs`. (4) Before committing, run `eng/list-skills.ps1` to confirm missing count = 0. (5) Close both backlog rows in the same PR via the reconcile step. |
| Scope | Production files touched (2): `skills/version-bump/SKILL.md`, `skills/workspace-health/SKILL.md`. Test files modified (1): `tests/RoslynMcp.Tests/Skills/SkillFrontmatterInstalledAsTests.cs` (remove `[Ignore]` only — no new methods). |
| Tool policy | edit-only |
| Estimated context cost | 15000 |
| Risks | (1) The `installed_as:` value must match `^(?:roslyn-mcp:)?[a-z][a-z0-9-]+$` (from `SkillFrontmatterInstalledAsTests.ValidInstalledAsPattern`). Both values `roslyn-mcp:version-bump` and `roslyn-mcp:workspace-health` satisfy this pattern. (2) Removing `[Ignore]` makes CI fail if any of the 46 SKILL.md files were missed in waves 1–11 — run `eng/list-skills.ps1` first to confirm 0 missing before the un-Ignore edit. (3) SKILL.md files are not compiled; no compilation risk. |
| Validation | (1) Run `eng/list-skills.ps1` — output must show 0 files missing `installed_as:`. (2) Run `dotnet test --filter SkillFrontmatterInstalledAsTests` — test must pass (was previously [Ignore]-skipped; now enforced). (3) Read both SKILL.md files to confirm `installed_as:` line is present and syntactically correct. |
| Performance review | N/A — doc/config-only changes, no hot-path modifications. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Completed `installed_as:` frontmatter migration (wave 12/12): added `installed_as:` to `skills/version-bump/SKILL.md` and `skills/workspace-health/SKILL.md`; removed `[Ignore]` from `SkillFrontmatterInstalledAsTests` to activate CI enforcement of the full 46-file contract. |
| Backlog sync | Close rows: skill-namespace-installed-as-wave-12, skill-namespace-installed-as-bulk-frontmatter-migration. |
