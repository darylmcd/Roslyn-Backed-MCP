# project-name-not-found-misleading-reload-advice — Report unknown project names and symbol handles as NotFound

**row:** `project-name-not-found-misleading-reload-advice` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceProjectResolver.cs:21`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:59`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`

## Acceptance

- [ ] build_project / test_run with projectName=NoSuchProject return a not-found error naming the loaded projects and never advise workspace_reload.
- [ ] rename_preview with a fabricated symbolHandle returns a not-found error instead of "Call workspace_reload if the state is stale".
- [ ] Regression test covers the projectName path through the tool error envelope.

## Evidence

- build_project/test_run(projectName=NoSuchProject) and rename_preview(fabricated symbolHandle) → "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry." Real cause: project/symbol not found (WorkspaceProjectResolver.cs:21). — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Sibling `unknown-projectname-silently-empty` covers the ProjectFilterHelper path (project_diagnostics/list_analyzers); reuse one message shape if both land.
