# detached-test-fixture-source-root-metadata — Preserve deterministic copied fixture compilation

**row:** `detached-test-fixture-source-root-metadata` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs` (`CopyRepositorySupportFiles`)
- `tests/RoslynMcp.Tests/TestFixtureFileSystemTests.cs`
- `tests/RoslynMcp.Tests/DetachedFixtureNativeContextIntegrationTests.cs` (self-contained four-route native CI probe)

## Acceptance

- [ ] Supply an actual top-level SourceRoot for every detached sample-copy variant while preserving copied compiler policy, CI, ContinuousIntegrationBuild and deterministic settings.
- [ ] Self-contained child-process CI regression fails on the original helper with the native deterministic SourceRoot error and configured Enable but loaded Disable. The corrected helper proves native error absence and configured options retained through all four real producer routes, actual serialized preview, apply and reload.
- [ ] Preserve original fixtures, source control metadata and existing assertions; no fake Git history, nullable forcing, diagnostic suppression or gate weakening. Preserve the original hosted CS8632 declaration cases in their dependent semantic slice; do not import their product changes into this prerequisite.

## Evidence

- PR1840 c5511d5c hosted run 40275116966: Windows2/Linux2 each fail nine cross-project nullable cases. Diagnostic head cb90656f run40282761123 confirms configured XML Enable but actual NullableContextOptions.Disable on both platforms; stronger fixture assertion exposes 109 cases per leg. Roslyn5.9 assembly MVIDs match local, ruling out a binary version difference.
- Current-session CI=true-only exact public-route probe captures native WORKSPACE_FAILURE: SourceRoot items must include at least one top-level (not nested) item when DeterministicSourcePaths is true. Incomplete compiler options then show Disable, with original generated CS8632. GITHUB_ACTIONS=true-only control retains Enable.
- Cold independent corrective control adds only actual fixture-root SourceRoot metadata to copied props while retaining CI/deterministic policy. Native SourceRoot error disappears; target retains Enable through preview/serialization/apply/reload and CS8632 disappears. Shared-helper repair plus all3copyvariant policy controls and exact4case child-CI regression are cold-approved and in progress; no row closed.
- Ordinary loader native failures are surfaced in status; this confirmed construction defect belongs to detached fixture metadata. Independent product nullable/language gaps remain separate selected rows. Eight selected rows total, within count10 ceiling.


- Operator sequencing 2026-10-26: deliver this independent fixture prerequisite first, then the base-token writer, then complete semantic/receiving slices. The rewritten native CI child owns its ordinary nullable Echo source and does not import parameter projection or handler tests. Full candidate remains preserved at f6c2e9f9f7b75bcb00c2bb5547ec5dafc818f9c9; no row closed.

Independent first slice: original helper native child is red on all four routes (top-level SourceRoot error and Contracts Disable). Receiving Contracts is initially empty, so this causal probe does not claim the old hosted CS8632 diagnostic; those declaration cases remain unchanged in the preserved candidate. Final frozen affected suite: 22 passed, zero failed, six existing link-capability skips; nested CI child exactly four passed and every other counter zero. Cold implementation review PASS; four changed C# files format clean; docs and compile eight projects pass. Hosted checks and exact committed-head validation remain pending.
