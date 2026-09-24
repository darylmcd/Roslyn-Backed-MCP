# editorconfig-write-no-workspace-invalidation — Invalidate the workspace after editorconfig writes

**row:** `editorconfig-write-no-workspace-invalidation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397`

## Acceptance

- [ ] After set_diagnostic_severity(CA1861, silent), project_diagnostics no longer reports CA1861 without a manual workspace_reload
- [ ] workspace_status isStale=true (or version bumped) after the write

## Evidence

- CA1861 still Info after set_diagnostic_severity silent; isStale=false. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
