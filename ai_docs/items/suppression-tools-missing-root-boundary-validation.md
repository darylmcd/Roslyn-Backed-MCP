# suppression-tools-missing-root-boundary-validation — pragma-suppression write tools bypass the sanctioned-root boundary

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SuppressionTools.cs`
- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs`

## Acceptance

- [ ] `add_pragma_suppression` and `pragma_scope_widen` validate their client-supplied `filePath` via `ClientRootPathValidator` inside the write gate, and forward the returned canonical target to `IEditService.ApplyTextEditsAsync`.
- [ ] One regression test per tool proves an out-of-boundary path is rejected.
- [ ] The two tools' behavior for in-boundary paths is unchanged (no regression in existing suppression tests).

## Evidence

Traced during the PR #1230 code-quality review, not hypothesized:

- `SuppressionTools.cs` contains NO `ClientRootPathValidator` reference at all (verified by grep over `src/RoslynMcp.Host.Stdio`).
- `ToolDispatch.PreviewWithWorkspaceIdAsync` performs no path validation either.
- `SuppressionService.cs` calls `IEditService.ApplyTextEditsAsync` at two sites, both defaulting the new `canonicalWritePath` parameter to `null`.

Net: both tools reach `EditService`'s physical disk write bounded only by workspace-document membership, with no configured-root check anywhere on the path. This is a wider hole than the validation-to-use race that `path-boundary-link-swap-toctou` addresses — that row's tools at least validate.

## Context

Surfaced while reviewing the (held) `path-boundary-link-swap-toctou` change, which added an optional
`canonicalWritePath` parameter to `IEditService.ApplyTextEditsAsync`. The reviewer enumerated every
production caller to check for silent-default hazards and found these two tools validate nothing.
Independent of whether that held initiative is revived — these tools need a boundary check either way.
