# Deep-review backlog intake

<!-- purpose: Backlog reconciliation for multi-repo campaigns; canonical home of the intake pipeline and staging semantics. -->
<!-- scope: reference -->

> **SCOPE: reference — context only, assigns no work.** Does not answer "next step?". Route via [`../planning_index.md`](../planning_index.md).

## Default (skill-driven)

Run the [`backlog-intake`](../../.claude/skills/backlog-intake/SKILL.md) skill from the Roslyn-Backed-MCP repo root:

```
/backlog-intake
```

Pipeline (canonical; other procedure docs link here):

1. **Stage** deep-review artifacts via `eng/stage-review-inbox.ps1` into `review-inbox/` (see [Staging semantics](#staging-semantics)).
2. **Extract** actionable items via a subagent (context-protecting).
3. **Deduplicate** semantically across files, not by literal text.
4. **Verify** each candidate against `CHANGELOG.md` [Unreleased] + last 3 versions + the newest remediation plan; drop items already shipped.
5. **Fix anchors** — cite real service-class files (`src/RoslynMcp.Roslyn/Services/`) plus the tool registration (`src/RoslynMcp.Host.Stdio/Tools/`).
6. **Split heroic rows** per `~/.claude/prompts/backlog-remediate-rules.md` (one code path, <=4 prod files, <=3 test files, one regression shape).
7. **Rank** into the backlog tiers `Critical` / `High` / `Medium` / `Low` (never `Defer` from raw severity) using correctness-risk bands.
8. **Write rows** through `node ~/.claude/scripts/backlog.mjs` (the only sanctioned backlog writer), then commit to a fresh branch off `main` (never pushed automatically).

## Staging semantics

Canonical: `eng/stage-review-inbox.ps1` `.DESCRIPTION`. Recognized shapes are listed there ("Recognized shapes"); do not re-list them elsewhere.

| Source | Default action | Override |
|--------|----------------|----------|
| Sibling repos | **MOVE** into `review-inbox/` (source deleted after staging) | `-CopyFromSiblings` copies instead |
| This repo (self) | **COPY** (canonical `ai_docs/audit-reports/` stays populated) | `-Move` forces move for self and siblings |

- Already-archived reports (`review-inbox/archive/<batch-ts>/`) are skipped, so re-runs are idempotent.
- `backlog.d/` fragments are not staged; `/backlog-intake` consumes them in place and deletes them at the source.

## Staging only

```powershell
./eng/stage-review-inbox.ps1                       # siblings move, self copies
./eng/stage-review-inbox.ps1 -DryRun               # preview only
./eng/stage-review-inbox.ps1 -CopyFromSiblings     # copy from siblings too (leave source repos untouched)
./eng/stage-review-inbox.ps1 -Move                 # move everywhere (clears self source too)
./eng/stage-review-inbox.ps1 -SkipSelf             # don't scan this repo's own ai_docs/
```

## Skill flags

| Flag | Effect |
|---|---|
| `--stage` | Force a staging pass even if `review-inbox/` already has files. |
| `--skip-stage` | Triage whatever is already in `review-inbox/`; don't scan siblings. |
| `--skip-verify` | Skip the CHANGELOG / plan / code cross-check (faster, riskier). |
| `--no-commit` | Write backlog rows but don't branch or commit. |
| `--sibling-parent <path>` | Override sibling scan root (forwarded to the PS1). |

## Manual overrides

- Rollups are optional: author one by hand under `ai_docs/reports/<timestamp>_deep-review-rollup.md` for release-gate sign-off; otherwise `review-inbox/` plus the backlog commit is the evidence trail.
- Re-opening a closed-and-re-reported issue or filing a narrative-only finding: add a fresh row with `node ~/.claude/scripts/backlog.mjs add`, never by editing `backlog.md`.

## Related

- [`deep-review-program.md`](deep-review-program.md) — program context (coverage matrix, client lanes, cadence).
- [`deep-review-command-reference.md`](deep-review-command-reference.md) — concrete commands.
- [`../../skills/mcp-server-surface-test/prompts/full.md`](../../skills/mcp-server-surface-test/prompts/full.md) — canonical audit prompt run by `/mcp-server-surface-test` (consumer) and `/mcp-server-stress` (maintainer alias for `--output-mode=fragments`).
- `~/.claude/prompts/backlog-remediate.md` and `backlog-remediate-rules.md` (global) — workflow contract that consumes the backlog.
