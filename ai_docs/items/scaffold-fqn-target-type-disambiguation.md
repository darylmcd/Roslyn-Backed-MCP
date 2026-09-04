# scaffold-fqn-target-type-disambiguation — Preserve qualified scaffold targets

**row:** `scaffold-fqn-target-type-disambiguation` · **pri:** `Medium` · **size:** `M` · **deps:** `scaffold-target-type-ambiguity-before-sampling`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs` (`StripToSimpleTypeName`, `GetMatchingTargetTypeCandidates`)
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs` (`ResolveTargetTypeAndMethodFromCache`)
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Dotted namespace-qualified input selects the exact matching symbol instead of being stripped before lookup.
- [ ] Simple-name input remains ambiguity-checked; generated identifiers stay valid simple names with the required namespace import.

## Regression

With two referenced types named `Widget`, simple `Widget` is rejected as ambiguous while `Alpha.Widget` selects only that symbol in both parameterized single and batch paths and renders `WidgetGeneratedTests`.
