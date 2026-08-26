# fixall-producer-kind-round-trip-coverage — assert fix_all_preview mints PreviewKind.FixAll

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs` (the `_previewStore.Store(..., kind: PreviewKind.FixAll)` call)
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs` (`compilation.WithAnalyzers(relevantAnalyzers)` — passes no `AnalyzerOptions`)
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs` (`CopyRepositorySupportFiles`)
- `tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs`

## Acceptance

- [ ] A test asserts that `fix_all_preview` mints a token whose recorded `PreviewKind` is `FixAll` — the producer half of the `fix_all_apply` route binding, currently unasserted anywhere in the suite.
- [ ] The test cannot pass without executing that assertion (no `Assert.Inconclusive` escape, no branch that skips it when the fixture yields no diagnostic).
- [ ] Whichever enabling change it needs is made deliberately: either `CopyRepositorySupportFiles` copies `.editorconfig` into the isolated workspace AND `FixAllService` passes `AnalyzerOptions` so editorconfig-driven IDE diagnostics fire, or the test anchors on a diagnostic that fires without editorconfig and has a registered FixAll provider.
- [ ] One regression shape: flipping `FixAllService`'s `Store` call to a kind-less overload fails the test.

## Evidence

Surfaced by three successive cold spec-compliance passes on PR #1375 (sweep `20260825T214500Z`, initiative `preview-token-route-binding-fileops-fixall`).

`PreviewKind.FixAll` is asserted nowhere in the suite as a *minted* value: the three `[DataRow(PreviewKind.FixAll)]` consumer cases run against a local `FakePreviewStore`, which is exactly the half that cannot catch a wrong-overload mint. `FixAllService`'s `Store` call is the single overload-switch in that change, so a wrong-overload edit compiles cleanly and silently records `Unspecified`.

Two attempts to close it inline failed, both for structural reasons rather than authoring mistakes:
1. Guarding a token-less run with `Assert.Inconclusive` — MSTest does not fail an inconclusive result, so the assertion was skipped on every run while reporting green.
2. Adding a block-scoped-namespace fixture file so IDE0161 would have an occurrence — unreachable, because `TestFixtureFileSystem.CopyRepositorySupportFiles` never copies `.editorconfig` into the isolated workspace and `FixAllService` passes no `AnalyzerOptions` to `WithAnalyzers`, so `csharp_style_namespace_declarations` resolves to the Roslyn default and IDE0161 reports zero occurrences. That attempt also collided by bare name with `DeadCodeIntegrationTests.Extension_Method_With_Callers_Not_Reported_As_Unused`.

## Context

The consumer half of the `fix_all_apply` binding IS covered and shipped; only the producer round-trip is missing. Sized S because the test itself is small — the judgement call is which enabling route to take, and that decision is the work.
