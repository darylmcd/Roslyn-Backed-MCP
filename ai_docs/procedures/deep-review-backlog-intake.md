# Deep-review backlog intake

<!-- purpose: Backlog reconciliation for multi-repo campaigns. -->
<!-- scope: reference -->

> **SCOPE: reference — context only, assigns no work.** Does not answer "next step?". Route via [`../planning_index.md`](../planning_index.md).

## Default (skill-driven)

Run the [`backlog-intake`](../../.claude/skills/backlog-intake/SKILL.md) skill from the Roslyn-Backed-MCP repo root:

```
/backlog-intake
```

The skill does end-to-end intake:

1. **Stages** deep-review artifacts by invoking `eng/stage-review-inbox.ps1`, which discovers `*_mcp-server-audit.md`, `*_experimental-promotion.md`, and `*_roslyn-mcp-retro.md` files across sibling repos + this repo's own `ai_docs/audit-reports/` + `ai_docs/reports/`, and moves them into `review-inbox/`.
2. **Extracts** actionable items via a subagent (context-protecting).
3. **Deduplicates** semantically across files (not literal-text matching).
4. **Verifies** each candidate against `CHANGELOG.md` [Unreleased] + last 3 versions + the newest backlog-sweep plan, dropping items that have already shipped.
5. **Fixes anchors** — rewrites each row to cite real service-class filenames (under `src/RoslynMcp.Roslyn/Services/`) plus the tool registration (under `src/RoslynMcp.Host.Stdio/Tools/`).
6. **Splits heroic rows** per `ai_docs/prompts/backlog-sweep-plan.md` Rule 1 (one code path, ≤4 prod files, ≤3 test files, one regression shape).
7. **Ranks** P2 / P3 / P4 using correctness-risk bands.
8. **Commits** to a fresh branch off `main` (never pushed automatically).

## Staging only

If you just want files moved into `review-inbox/` without triage:

```powershell
./eng/stage-review-inbox.ps1
./eng/stage-review-inbox.ps1 -DryRun        # preview only
./eng/stage-review-inbox.ps1 -Copy          # copy instead of move
```

## Skill flags

| Flag | Effect |
|---|---|
| `--stage` | Force a staging pass even if `review-inbox/` already has files. |
| `--skip-stage` | Triage whatever is already in `review-inbox/`; don't scan siblings. |
| `--skip-verify` | Skip the CHANGELOG / plan / code cross-check (faster, riskier). |
| `--no-commit` | Write backlog.md but don't branch or commit. |
| `--sibling-parent <path>` | Override sibling scan root (forwarded to the PS1). |

## Manual overrides

- A rollup markdown scaffold is no longer required. If you want one for a release-gate sign-off, author it by hand under `ai_docs/reports/<timestamp>_deep-review-rollup.md` — the review-inbox files + the backlog commit together serve as the evidence trail for routine batches.
- To re-open an issue that was closed and re-reported, add it to `ai_docs/backlog.md` directly with a fresh id.

## Related

- [`deep-review-program.md`](deep-review-program.md) — program context (coverage matrix, client lanes, cadence).
- [`deep-review-command-reference.md`](deep-review-command-reference.md) — concrete commands.
- [`../prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) — per-repo raw audit prompt that produces the artifacts this skill consumes.
- [`../prompts/backlog-sweep-plan.md`](../prompts/backlog-sweep-plan.md) — planner prompt that consumes the backlog this skill produces.
