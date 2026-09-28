# project-name-not-found-misleading-reload-advice — Stop advising workspace_reload for unknown project names and symbol handles

**row:** `project-name-not-found-misleading-reload-advice` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceProjectResolver.cs:21`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:59`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`

## Acceptance

- [ ] build_project / test_run with projectName=NoSuchProject return an error whose message says the project was not found, names the loaded projects and never advises workspace_reload.
- [ ] rename_preview with a fabricated symbolHandle returns an error whose message says the handle did not resolve to a symbol, instead of "Call workspace_reload if the state is stale".
- [ ] Compatibility class: minor-compatible, so the row ships on the 4.x line. Both refusals keep category `InvalidOperation`, `isError` and `exceptionType` `InvalidOperationException`; only the message changes, through `PublicInvalidOperationException` (`ToolErrorHandler.cs:152-157`). Moving them to a not-found category changes a stable category value, which is a major-version change (`docs/release-policy.md:24-26`, `:36`; precedent `changelog.d/workspace-id-unknown-error-category.md`). That half is the Defer row `project-name-not-found-category-next-major`.
- [ ] `exceptionType` holds because this row depends on `public-argument-exception-core-move`. The envelope writes `ex.GetType().Name` (`ToolErrorHandler.cs:670`), so without that row's marker normalization (its Acceptance bullet 3) the conversion would report `PublicInvalidOperationException` and flip back when core-move lands.
- [ ] Regression test covers the projectName path through the tool error envelope and asserts the category, the message and `exceptionType == "InvalidOperationException"`.

## Evidence

- build_project/test_run(projectName=NoSuchProject) and rename_preview(fabricated symbolHandle) → "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry." Real cause: project/symbol not found (WorkspaceProjectResolver.cs:21). — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Sibling `unknown-projectname-silently-empty` covers the ProjectFilterHelper path (project_diagnostics/list_analyzers); reuse one message shape if both land.

## Notes

- 2026-09-28: mechanism parent `tool-refusal-public-message-guard` adds a ratchet that fails any new plain `InvalidOperationException` under `src/`; this row is part of its burn-down. Convert this row's sites to `PublicInvalidOperationException` (or the Core internal factory for true invariants) and lower the committed baseline count in the same PR.
- 2026-09-28 (PR #1655 review round 4): the operator chose an additive 4.x release, so this row keeps category `InvalidOperation` and fixes only the message. The `NotFound` category in the original title is a breaking change and moved to the Defer row `project-name-not-found-category-next-major`.
- 2026-09-28 (PR #1655 review round 5): retitled to the 4.x scope, and now depends on `public-argument-exception-core-move` so `exceptionType` does not change.
