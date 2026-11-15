# moved-type-receiver-namespace-name-conflict — Refuse receiver namespace and type declaration-space conflicts

**row:** `moved-type-receiver-namespace-name-conflict` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`PreviewMoveTypeToProjectAsync`)
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs` (`PreviewChangeTypeNamespaceAsync`)
- `src/RoslynMcp.Roslyn/Helpers/MovedTypeDestinationIdentity.cs` (`EnsureAvailableAsync`)
- `tests/RoslynMcp.Tests/MovedTypeDestinationCollisionIntegrationTests.cs`

## Acceptance

- [ ] Through both experimental move previews, refuse when moving arity-zero `MoveProbe` next to `namespace Destination.MoveProbe { }` introduces CS0101. Prove the refusal occurs before token storage and preserves workspace and disk state.
- [ ] Keep the arity-one control `MoveProbe<T>` when the compiler permits the distinct declaration-space entry. Use the receiver's actual projected namespace and compiler identity.

## Evidence

- Cold review compiler probe on the receiver-identity fix confirmed CS0101 with a namespace of the same arity-zero name. The generic arity-one control compiled. `INamespaceSymbol.GetTypeMembers` excludes namespace declarations, so validating only clashes between two declared types leaves this namespace/type rule untested.
