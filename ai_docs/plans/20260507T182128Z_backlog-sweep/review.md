# Plan review — 2026-05-07T18:25:00Z

**Plan reviewed:** ai_docs/plans/20260507T182128Z_backlog-sweep/
**Reviewer mode:** /backlog-sweep:review
**Outcome:** **failed**
**Initiative count:** 5
**Findings:** block: 2, warn: 3, info: 5
**Anchor verification:** performed (grep + ls of every cited path during plan write)

## Summary

The plan is thematically sound and the row→initiative mapping is 1:1, but two initiatives violate Rule 3's hard 4-file cap. Initiative 1 only fits if you accept a planner-introduced "pure git-mv excluded from production-file count" exemption that the addenda does not authorize (the addenda explicitly says global ceilings apply unmodified). Initiative 2 has an internal contradiction — the Scope header claims 4 production files but the listed files plus the planner's own arithmetic in the same field total 5. Both must be split or re-scoped before execute can run.

The conflict graph is also tight: every pair of adjacent-order initiatives shares at least one file (`SKILL.md`, `publish-preflight/SKILL.md`, or `mcp-server-stress/prompt.md`), so this sweep cannot run parallel waves — it must serialize. Not a block, but the executor should know.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| `mcp-server-stress-relocate` | **block** | 3 | `state.json.productionFilesTouched=4` excludes 4 pure git-mv files (`prompts/full.md`, `prompts/promotion-only.md`, `prompts/read-only.md`, `scripts/archive-old-reports.ps1`) per planner judgment. Strict reading of Rule 3 ("touches ≤ 4 production files") counts every touched file regardless of content delta; addenda does not list a rename exemption ("Repo-specific overrides (none currently). The global Rules 1–5 ceilings apply unmodified."). True count is 8 production touches. Either: (a) split the relocate into "directory move" and "external-references" initiatives so neither exceeds 4, or (b) the user sets `state.json.vettingOverride=true` to accept the planner's mechanical-rename framing. |
| `mcp-server-stress-single-mode` | **block** | 3 | Plan Scope contradicts itself: header says "Production files: 4" but enumerates 5 file paths and the embedded "Counts: 2 edited + 2 deleted + 1 edited = 5" arithmetic agrees with 5. Files: `.claude/skills/mcp-server-stress/SKILL.md`, `prompt.md`, `prompts/promotion-only.md` (delete), `prompts/read-only.md` (delete), `.claude/skills/publish-preflight/SKILL.md`. Recommendation: split the publish-preflight edit (which only updates the `mode=...` qualifiers) into a follow-on initiative or fold it into the relocate's external-references initiative. |
| `mcp-server-stress-relocate` ↔ `mcp-server-stress-single-mode` | warn | wave-conflict | Adjacent-order initiatives share 2 files (`.claude/skills/mcp-server-stress/SKILL.md`, `.claude/skills/publish-preflight/SKILL.md`). Planner Step 6 sort should have separated; in serial mode this is fine but parallel-wave scheduling is impossible. |
| `mcp-server-stress-single-mode` ↔ `extract-skills-audit-from-server-stress` | warn | wave-conflict | Adjacent-order initiatives share `.claude/skills/mcp-server-stress/prompt.md` (init 2 creates it via rename from full.md, init 3 edits it). Init 3 strictly cannot start before init 2 ships. |
| `backlog-d-fragment-pattern` ↔ `per-repo-promotion-scorecard` | warn | wave-conflict | Adjacent-order initiatives share `.claude/skills/mcp-server-stress/prompt.md`. The plan notes call this out ("schedule serially or coordinate edit regions") but the planner's Step 6 sort should have inserted a non-conflicting initiative between them, or reordered. |
| all 5 | info | wave-conflict | Conflict-degree ≥ 2 for every initiative (degrees: 1→2, 2→4, 3→3, 4→3, 5→4). This sweep must run serially. Executor should not attempt parallel mode. |

## Conflict graph

```
edges:
  - {a: mcp-server-stress-relocate,            b: mcp-server-stress-single-mode,           sharedFiles: [.claude/skills/mcp-server-stress/SKILL.md, .claude/skills/publish-preflight/SKILL.md]}
  - {a: mcp-server-stress-relocate,            b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/publish-preflight/SKILL.md]}
  - {a: mcp-server-stress-single-mode,         b: extract-skills-audit-from-server-stress, sharedFiles: [.claude/skills/mcp-server-stress/prompt.md]}
  - {a: mcp-server-stress-single-mode,         b: backlog-d-fragment-pattern,              sharedFiles: [.claude/skills/mcp-server-stress/prompt.md]}
  - {a: mcp-server-stress-single-mode,         b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/mcp-server-stress/prompt.md, .claude/skills/publish-preflight/SKILL.md]}
  - {a: extract-skills-audit-from-server-stress, b: backlog-d-fragment-pattern,            sharedFiles: [.claude/skills/mcp-server-stress/prompt.md]}
  - {a: extract-skills-audit-from-server-stress, b: per-repo-promotion-scorecard,          sharedFiles: [.claude/skills/mcp-server-stress/prompt.md]}
  - {a: backlog-d-fragment-pattern,            b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/mcp-server-stress/prompt.md]}

degrees:
  mcp-server-stress-relocate:              2
  mcp-server-stress-single-mode:           4
  extract-skills-audit-from-server-stress: 3
  backlog-d-fragment-pattern:              3
  per-repo-promotion-scorecard:            4
```

## Recommended next step

Two paths:

1. **Re-plan (recommended):** `/backlog-sweep:plan plan-id=20260507T182128Z_backlog-sweep` and rebalance:
   - Split initiative 1 into `mcp-server-stress-relocate-files` (directory move + SKILL.md frontmatter edit, ≤4 files of content edits) and `mcp-server-stress-update-external-refs` (publish-preflight + audit-phase-runner + orphan-doc delete, ≤3 files).
   - Move the `publish-preflight/SKILL.md` mode-qualifier edit out of initiative 2 into the new external-refs initiative — drops init 2 to 4 production files cleanly.
   - Reorder so adjacent-order initiatives share the fewest files possible (e.g. interleave `extract-skills-audit-from-server-stress` between the relocate-and-rename pair and the prompt-content rewrite, since it doesn't conflict with `mcp-server-stress/SKILL.md`).

2. **Override:** if the user accepts the planner's "pure git-mv excluded" framing for init 1 and accepts that init 2's Scope arithmetic was an internal accounting error that doesn't change the executor's actual file budget (5 files, one of which is a single-line description-text update), set `state.json.vettingOverride = true` and proceed. This is faster but normalizes the rule violation.
