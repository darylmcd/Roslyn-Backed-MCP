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
## Amendment — 2026-08-13 (cold self-review of sweep 20260813T172325Z)

**Acceptance re-based onto the HEAD signature.** The Evidence and Acceptance above were written against `IEditService.ApplyTextEditsAsync`'s `canonicalWritePath` parameter — that parameter exists ONLY on the held PR #1230 branch and is **not at HEAD** (verified: a repo grep for `canonicalWritePath` over `src`/`tests` returns nothing).

The row's substantive finding is unaffected and still verified: `SuppressionTools.cs` contains no `ClientRootPathValidator` reference, and both `SuppressionService` write call sites reach `EditService`'s physical write with only workspace-document membership as a bound.

**Read the acceptance as:** `add_pragma_suppression` and `pragma_scope_widen` must validate their client-supplied `filePath` against the configured root boundary before reaching the write, and must pin the validated target through to it — by whatever mechanism exists when this row is worked. If `path-boundary-link-swap-toctou` is re-planned and lands a canonical-path-carrying signature first, reuse it; if not, this row supplies its own. Do NOT treat the held branch's parameter as an existing API.
