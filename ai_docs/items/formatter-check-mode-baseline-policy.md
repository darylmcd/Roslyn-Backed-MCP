# Formatter Check-Mode Baseline Policy

<!-- purpose: Record the current formatter check-mode baseline and the first bounded follow-on. -->
<!-- scope: in-repo -->

## Command

`dotnet format RoslynMcp.slnx --verify-no-changes --no-restore`

Run from the repository root on 2026-05-22. Exit code: 1.

## Findings

- `WHITESPACE`: broad formatting-only drift across host, Roslyn, and test files. Representative anchors from the baseline output include `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`, and `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`.
- `IDE1006`: broad private-field naming drift where existing fields do not use the configured underscore prefix. Representative anchors include `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`, and `tests/RoslynMcp.Tests/TestBase.cs`.

## Decision

Do not make `dotnet format --verify-no-changes` a merge gate yet. The current check fails on broad pre-existing debt that is unrelated to most feature PRs, so gating it now would make unrelated changes responsible for repo-wide churn.

Normalize whitespace separately from analyzer naming debt. Whitespace-only fixes are mechanical and low risk when bounded by file set. The `IDE1006` findings cross many production and test files and should remain a separate policy decision because renaming fields has higher review noise and merge-conflict risk.

The current merge gates remain the documented repository gates in `CI_POLICY.md`; this note only records formatter check-mode readiness.

## Follow-On Row

Add `formatter-host-stdio-whitespace-slice` as a bounded backlog row. The first implementation slice should run formatter whitespace normalization only for a small Host.Stdio file set named in the baseline, then rerun the formatter check with `--include` for those exact files. Do not include `IDE1006` naming cleanup in that row.
