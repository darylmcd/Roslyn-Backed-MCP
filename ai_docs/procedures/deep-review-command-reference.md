# Deep-review command reference

<!-- purpose: Operator commands for the multi-repo deep-review pipeline. -->

Run from the **Roslyn-Backed-MCP** repo root:

```powershell
Set-Location C:\Code-Repo\Roslyn-Backed-MCP
```

## Produce audits (per repo)

Run the audit prompt inside the repo being audited. It emits raw deep-review artifacts into that repo's `ai_docs/audit-reports/` or `ai_docs/reports/` (recognized shapes: `eng/stage-review-inbox.ps1` `.DESCRIPTION`).

```
/mcp-server-stress
```

Produce audits across every sibling C# repo in the coverage matrix before running intake.

## Intake audits into the backlog

```
/backlog-intake
```

Pipeline, staging move/copy semantics, and flags: [`deep-review-backlog-intake.md`](deep-review-backlog-intake.md).

```
/backlog-intake --stage            # force a staging pass even if review-inbox/ has files
/backlog-intake --skip-stage       # triage only what's already in review-inbox/
/backlog-intake --skip-verify      # skip CHANGELOG / plan cross-check (faster, riskier)
/backlog-intake --no-commit        # write backlog rows but don't branch or commit
/backlog-intake --sibling-parent C:\Customer-Repos
```

## Staging only (no triage)

```powershell
./eng/stage-review-inbox.ps1                       # siblings move, self copies (see intake doc)
./eng/stage-review-inbox.ps1 -DryRun               # preview only
./eng/stage-review-inbox.ps1 -CopyFromSiblings     # copy from siblings instead of moving
./eng/stage-review-inbox.ps1 -SkipSelf             # don't scan this repo's own ai_docs/
```

## Verify AI docs links

```powershell
./eng/verify-ai-docs.ps1
```

## Related

- [`deep-review-program.md`](deep-review-program.md) — coverage matrix and program rules
- [`deep-review-backlog-intake.md`](deep-review-backlog-intake.md) — intake pipeline and staging reference
- [`../audit-reports/README.md`](../audit-reports/README.md) — raw audit storage
- [`../reports/README.md`](../reports/README.md) — rollup storage
