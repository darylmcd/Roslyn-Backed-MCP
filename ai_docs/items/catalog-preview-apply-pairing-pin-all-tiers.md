# catalog-preview-apply-pairing-pin-all-tiers — pin the preview-to-apply catalog pairing regardless of support tier

## Anchors

- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs` (the existing pairing invariant, filtered to `SupportTier == "stable"`)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (`PreviewApplyRoutes`)

## Acceptance

- [ ] A test asserts every catalog tool named `*_preview` whose sibling `*_apply` entry exists in `ServerSurfaceCatalog.Tools` is a key in `PreviewApplyRoutes`, with no support-tier filter.
- [ ] One regression shape: removing a `PreviewApplyRoutes` entry for an experimental preview fails that test.

## Evidence

Cold code-quality review of PR #1377 (sweep `20260825T214500Z`) verified the only existing pairing invariant filters `SupportTier == "stable"`, and a repo-wide search found no other `PreviewApplyRoutes` coverage. That filter is exactly why the missing `extract_interface_preview` entry survived undetected — both `extract_interface_preview` and `extract_interface_apply` are `experimental`, so the stable-only pin never looked at them. The gap only surfaced because a new `PreviewKind` member forced `ApplyRouteFor` to throw.

Without an all-tier pin the next omission repeats silently.

## Context

Also the mechanism behind a prepare-time planning error in this sweep: the plan asserted the catalog already covered all four extraction preview tools when it covered three.
