# Plan review — 2026-05-07T18:42:00Z (revised pass)

**Plan reviewed:** ai_docs/plans/20260507T182128Z_backlog-sweep/ (revision 2026-05-07T18:36:28Z)
**Reviewer mode:** /backlog-sweep:review
**Outcome:** **passed-with-warnings**
**Initiative count:** 6
**Findings:** block: 0, warn: 2, info: 4
**Anchor verification:** performed (anchors cited in initiatives 1-3 verified to exist in current source tree)

## Summary

The re-plan resolves both block findings from the previous review pass. Every initiative is strictly within Rule 3 (≤4 prod files) with no exemptions invoked, Rule 4 (≤3 test files), and Rule 5 (≤80K context). The "collapse modes first in old location" reordering is the right structural fix — it shrinks the source directory before the move so the relocate is mechanically a 3-file operation. The remaining findings are about post-relocate file-path inconsistency in initiatives 4-6 (`prompt.md` vs `prompts/prompt.md`), and adjacent-order conflicts on the prompt file (4↔5, 5↔6). Neither blocks execution but the executor will need to serialize 4-5-6 and resolve the path question.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| `extract-skills-audit-from-server-stress` ↔ `backlog-d-fragment-pattern` | warn | wave-conflict | Adjacent-order initiatives (orders 4 and 5) share `.claude/skills/mcp-server-stress/prompt.md`. Cannot run in same parallel wave; init 5 must wait for init 4 (and the conflict-tracking note in state.json `notes` field acknowledges this). |
| `backlog-d-fragment-pattern` ↔ `per-repo-promotion-scorecard` | warn | wave-conflict | Adjacent-order initiatives (orders 5 and 6) share `.claude/skills/mcp-server-stress/prompt.md`. Same constraint — serial ordering required between 5 and 6. The plan's notes acknowledge it; the planner's Step 6 sort *could* have placed `extract-skills-audit-from-server-stress` (no prompt.md conflict with publish-preflight or aggregator paths) between 5 and 6, but the dependency chain (4→5→6 by content) probably justifies the current order anyway. |
| inits 4, 5, 6 | info | wave-conflict | Conflict-degree ≥ 2: init 4 (degree 2 — with 5, 6), init 5 (degree 2 — with 4, 6), init 6 (degree 3 — with 3, 4, 5). All three center on the same shared file. Sweep cannot run parallel-mode for 4-5-6; serial scheduling is required. |
| inits 4, 5, 6 | info | path | Plan text for inits 4-6 references `.claude/skills/mcp-server-stress/prompt.md` (no `prompts/` subdir). Inits 1 and 2 explicitly preserve the `prompts/` subdir (init 1: "rename `skills/audit-deep/prompts/full.md` → `skills/audit-deep/prompts/prompt.md`"; init 2: "moves 3 files atomically: SKILL.md, prompts/prompt.md, scripts/archive-old-reports.ps1"). Either inits 4-6 contain a path typo (intent: `.claude/skills/mcp-server-stress/prompts/prompt.md`) OR the planner intended a single-prompt-skill simplification (drop `prompts/` subdir, prompt at skill root) that wasn't made explicit in any initiative's Approach. Executor should resolve at run time — recommend treating as `.claude/skills/mcp-server-stress/prompts/prompt.md` (preserve original layout) unless the user wants the simplification, in which case fold it into init 2's Approach. |
| `backlog-d-fragment-pattern` | info | testing | testFilesAdded=0 for an initiative that materially changes `/backlog-intake` (consolidation logic, fragment dedup, source-deletion). Validation relies on manual smoke tests against synthetic sibling repos. Risk: regression in intake behavior won't be caught by CI. Plan body validates this with hand-driven scenarios but doesn't add a test file. Consider adding 1 test file in the executor's discretion (still within Rule 4's ≤3 cap). |
| `mcp-server-stress-relocate` | info | path-discipline | This initiative `git mv`s the directory and edits SKILL.md frontmatter. Risk: if the executor uses `mv` instead of `git mv`, git tracking breaks and the move shows as delete + create rather than rename. Approach is correct (`git mv`) but the executor should treat this as a hard requirement, not a stylistic choice. |

## Conflict graph

```
edges:
  - {a: mcp-server-stress-update-external-refs,   b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/publish-preflight/SKILL.md]}
  - {a: extract-skills-audit-from-server-stress,  b: backlog-d-fragment-pattern,              sharedFiles: [.claude/skills/mcp-server-stress/prompts/prompt.md]}
  - {a: extract-skills-audit-from-server-stress,  b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/mcp-server-stress/prompts/prompt.md]}
  - {a: backlog-d-fragment-pattern,               b: per-repo-promotion-scorecard,            sharedFiles: [.claude/skills/mcp-server-stress/prompts/prompt.md]}

degrees:
  audit-deep-collapse-modes:                0
  mcp-server-stress-relocate:               0
  mcp-server-stress-update-external-refs:   1
  extract-skills-audit-from-server-stress:  2
  backlog-d-fragment-pattern:               2
  per-repo-promotion-scorecard:             3
```

The graph is much sparser than the previous version (8 edges → 4 edges). Inits 1 and 2 have zero conflicts because the relocate happens *between* them — init 1 lives in `skills/audit-deep/` and init 2 moves to `.claude/skills/mcp-server-stress/`, so the file paths in the diffs don't overlap. This is the cleanest part of the re-plan.

## Recommended next step

`passed-with-warnings`. The 2 warns are about post-relocate adjacent-order prompt.md conflicts (4↔5, 5↔6) — the planner is aware (state.json `notes`), and serial scheduling is the only valid mode here regardless. The 4 infos are advisory, not blocking.

Proceed to `/backlog-sweep:execute`. Recommended toggles:
- **Serial mode** (no parallel waves) — the conflict graph forces serialization for inits 4-6 anyway, and inits 1-3 are a sequential chain by construction (1 must complete before 2's move finds the right files; 2 must complete before 3's external-ref updates point at the new path).
- Have the executor confirm the `.claude/skills/mcp-server-stress/prompt.md` vs `.claude/skills/mcp-server-stress/prompts/prompt.md` path question in init 2 (where the move first happens) and propagate the chosen layout into inits 4-6.
- Consider adding 1 test file in init 5 (`tests/RoslynMcp.Tests/Skills/BacklogIntakeFragmentTests.cs` or similar) to give the fragment-pattern change CI coverage. Still within Rule 4.
