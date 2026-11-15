# namespace-deps-bind-import-targets — Resolve namespace graph import endpoints semantically

**row:** `namespace-deps-bind-import-targets` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs`
- `src/RoslynMcp.Core/Models/NamespaceDependencyDto.cs`
- `tests/RoslynMcp.Tests/NamespaceDependencyOwnershipTests.cs`

## Acceptance

- [ ] Qualify release impact before selection. Define endpoint behavior for bound namespace imports, aliases and unresolved names. Cover relative and globally qualified forms and suppress bound self-edges.

## Evidence

- Cold review reproduced with Roslyn 5.9.0. Within `P.R`, `using Q;` binds to `P.Q` and `using global::P.R;` binds back to `P.R`. The stable graph currently emits the raw targets `Q` and `global::P.R`. The latter evades self-edge suppression.

## Context

- Existing clients receive raw endpoint names. Resolve compatibility before changing the stable payload.
