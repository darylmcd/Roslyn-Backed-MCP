# Deep-review command reference

<!-- purpose: Operator commands for the multi-repo deep-review pipeline. -->

Run from the **Roslyn-Backed-MCP** repo root:

```powershell
Set-Location C:\Code-Repo\Roslyn-Backed-MCP
```

## Produce audits (per repo)

Run the audit prompt inside the repo being audited. The prompt emits a raw `*_mcp-server-audit.md` (and, when applicable, `*_experimental-promotion.md` / `*_roslyn-mcp-retro.md`) into that repo's `ai_docs/audit-reports/` or `ai_docs/reports/`.

```
/deep-review-and-refactor
```

Produce audits across every sibling C# repo in the coverage matrix before running intake.

## Intake audits into the backlog (one command)

From Roslyn-Backed-MCP repo root:

```
/backlog-intake
```

This runs the [`backlog-intake`](../../.claude/skills/backlog-intake/SKILL.md) skill, which:

1. Stages `*_mcp-server-audit.md` / `*_experimental-promotion.md` / `*_roslyn-mcp-retro.md` from sibling repos + this repo into `review-inbox/`.
2. Extracts actionable items via a subagent (context-protecting).
3. Deduplicates semantically across files.
4. Verifies each candidate against `CHANGELOG.md` [Unreleased] + last 3 versions + the newest backlog-sweep plan.
5. Fixes service / tool anchors so each row lands on real files.
6. Splits heroic rows per `backlog-sweep-plan.md` Rule 1 / 3 / 4.
7. Ranks P2 / P3 / P4.
8. Commits to a fresh branch off `main`.

## Skill flags

```
/backlog-intake --stage            # force a staging pass even if review-inbox/ has files
/backlog-intake --skip-stage       # triage only what's already in review-inbox/
/backlog-intake --skip-verify      # skip CHANGELOG / plan cross-check (faster, riskier)
/backlog-intake --no-commit        # write backlog.md but don't branch or commit
/backlog-intake --sibling-parent C:\Customer-Repos
```

## Staging only (no triage)

```powershell
./eng/stage-review-inbox.ps1                 # move audit/retro/promotion files into review-inbox/
./eng/stage-review-inbox.ps1 -DryRun         # preview only
./eng/stage-review-inbox.ps1 -Copy           # copy instead of move
./eng/stage-review-inbox.ps1 -SkipSelf       # don't scan this repo's own ai_docs/
```

## Verify AI docs links

```powershell
./eng/verify-ai-docs.ps1
```

## Related

- [`deep-review-program.md`](deep-review-program.md) — coverage matrix and program rules
- [`deep-review-backlog-intake.md`](deep-review-backlog-intake.md) — intake skill reference
- [`../audit-reports/README.md`](../audit-reports/README.md) — raw audit storage
- [`../reports/README.md`](../reports/README.md) — rollup storage
