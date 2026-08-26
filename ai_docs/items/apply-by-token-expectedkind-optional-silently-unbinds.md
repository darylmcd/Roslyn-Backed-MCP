# apply-by-token-expectedkind-optional-silently-unbinds — make the preview-token provenance binding non-optional

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs` (`ApplyByTokenAsync`, `PreviewKind expectedKind = PreviewKind.Unspecified`)
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`

## Acceptance

- [ ] Dropping the `expectedKind:` argument at an `*_apply` shim call site can no longer compile green — either the parameter is required at every call site, or an assembly-wide reflection test asserts every `*_apply` shim passes a concrete (non-`Unspecified`) kind.
- [ ] The guard does not depend on each tool family remembering to author its own per-family test file.
- [ ] One regression shape: a call site with the binding removed fails the build or that single assembly-wide test.

## Evidence

Surfaced by the cold spec-compliance re-review of initiative `preview-token-route-binding-extraction-family` (PR #1377) during sweep `20260825T214500Z`. `ApplyByTokenAsync` declares `PreviewKind expectedKind = PreviewKind.Unspecified` as an OPTIONAL parameter, so removing the binding at any call site compiles cleanly and silently reverts that route to permissive redemption — precisely the defect the whole `preview-token-apply-route-binding-remaining-families` family exists to close. No compiler error and no analyzer catches it. The reviewer noted the new test file's own remarks concede the hazard ("A regression that drops the argument compiles green — the parameter is optional — and would only be caught here"), which means the guard currently rests on per-family test files rather than on the type system.

## Context

Deliberately NOT fixed inside the route-binding children: it is out of their Scope and would breach their gate-forced file budgets. File after the family lands so the fix can cover every bound route at once.
