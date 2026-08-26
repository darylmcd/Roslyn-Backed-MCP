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

## Amendment — 2026-08-26 (cold code-quality review of PR #1383)

A second defect lives on the same edit surface — fold it into this row rather than filing a sibling, since both rewrite `ApplyByTokenAsync`'s parameter list across every `*_apply` call site and two rows would collide as PRs.

Add to Acceptance:

- [ ] `RequireCompatibleProducer`'s mismatch message names the route the caller **actually invoked**, not `ApplyRouteFor(expectedKind)`.
- [ ] One regression shape: a foreign-family token redeemed through `scaffold_type_apply` produces a message containing `scaffold_type_apply`, matching the invoked-route assertion the file-ops and editing route-binding test files already make.

**Evidence (traced, not hypothesized).** `scaffold_type_apply` and `scaffold_test_apply` bind to `expectedKind: PreviewKind.FileCreate` because that is genuinely the producer kind. But the refusal message is built by deriving the route name from the kind: `PreviewToolFor(FileCreate)` returns `create_file_preview`, and `PreviewApplyRoutes["create_file_preview"]` is `create_file_apply`. So redeeming a foreign token through `scaffold_type_apply` reports *"not `create_file_apply`, which only accepts `create_file_preview` tokens"* — naming a route the caller never invoked.

All six other routes bound during this sweep derive their own name correctly; these two are the first divergence, and they are divergent precisely because a producer kind can be shared by more than one apply route. Deriving the invoked route from the expected kind is therefore wrong in general, not just here.

The scaffolding test added by #1383 asserts only the `rename_apply` half of the message and skips the invoked-route assertion its two sibling route-binding test files do make, which is why this shipped green.
