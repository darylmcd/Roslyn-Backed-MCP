# project-name-not-found-category-next-major — At the next major, move unknown-project and unknown-handle refusals off the InvalidOperation category

**row:** `project-name-not-found-category-next-major` · **pri:** `Defer` · **size:** `M` · **deps:** `project-name-not-found-misleading-reload-advice`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceProjectResolver.cs:21`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:59`
- `docs/decisions/README.md`
- `docs/product-contract.md`

## Acceptance

- [ ] `build_project` and `test_run` with an unknown `projectName`, and `rename_preview` with a fabricated `symbolHandle`, return a not-found category instead of `InvalidOperation`. The ADR picks `NotFound`, or `InvalidArgument` to match `compile_check`'s unknown `projectName` refusal (`CompileCheckTools.cs:65-70`) and the merged, unreleased `unknown-projectname-silently-empty` fix.
- [ ] Ships only in a major release, with an ADR in `docs/decisions/`, a `docs/product-contract.md` entry, and a `Changed — BREAKING` changelog fragment with a migration note (`docs/release-policy.md:24-26`, `:36`).
- [ ] A wire test pins the new category on each of the three paths.

## Evidence

- `WorkspaceProjectResolver.cs:21` and `RefactoringService.cs:59` throw plain `InvalidOperationException`, which `ToolErrorHandler` classifies as `InvalidOperation`.
- The repo classifies a category change on an existing error path as breaking: `changelog.d/workspace-id-unknown-error-category.md` (unknown `workspaceId`, `NotFound` to `WorkspaceNotFound`) is `Changed — BREAKING`.

## Context

- Split from `project-name-not-found-misleading-reload-advice` in PR #1655 review round 4. The operator chose an additive 4.x release, so that row fixes the message and keeps the category; this row carries the category change to the next major.
