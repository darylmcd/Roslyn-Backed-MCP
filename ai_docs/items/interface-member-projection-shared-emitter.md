# interface-member-projection-shared-emitter — One symbol-driven projector for both interface extractors

**row:** `interface-member-projection-shared-emitter` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:189` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:499` (`CreateInterfaceMember`, `CreateParameterList`, `CreateAccessorList`)
- (new) `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs`
- (new) `tests/RoslynMcp.Tests/InterfaceMemberProjectorTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Both extractors obtain every emitted member from one projector; neither keeps a per-construct `SyntaxFactory` switch of its own.
- [ ] The projector derives each member from the symbol's own declaration (declaring syntax, or `SyntaxGenerator.Declaration` where no syntax exists) and strips bodies, accessibility and implementation-only modifiers, so ref/ref readonly returns, indexers, generic parameters with constraints, events, get/set/init accessors, escaped identifiers, unsafe pointer types, parameter modifiers and defaults, parameter attributes, nested and substituted generic type names and nullable annotations carry over without a per-construct branch.
- [ ] A construct-matrix test compiles a source type containing every construct above, extracts its interface through both services, and proves the source type implements the emitted interface with zero diagnostics; it fails on the current emitters.
- [ ] Every symptom row chained behind this one (`deps`) is closed as subsumed when its acceptance passes through the projector, or kept open with only its remaining delta named.

## Evidence

- `backlog.mjs hotspots` 2026-10-28: `CrossProjectRefactoringService.cs` 22 live rows (interface-extraction:9, cross-project:6) and `InterfaceExtractionService.cs` 16 rows, both `no-structural-row`; eleven of those rows each add one C# construct to the same two hand-rolled emitters.

## Context

The two emitters duplicate the same shape: a `switch` on `ISymbol` kind that reassembles syntax from `ToDisplayString` fragments. Each missing construct (ref returns, indexers emitted as `this[]` properties, dropped generic parameters, dropped events, lost attributes, lost nullable context) became its own row, and the family grows with every new construct a user hits. The structural fix is one projector, so the per-construct rows become its test matrix instead of eleven separate fixes.

Symptom rows chained behind this row: `cross-project-interface-parameter-semantics`, `interface-extraction-ref-return-contracts`, `interface-extraction-unsafe-signature-context`, `interface-extraction-escaped-member-identifiers`, `interface-extraction-nullability-context-fidelity`, `interface-extraction-indexer-declarations`, `interface-extraction-parameter-contract-attributes`, `cross-project-interface-generic-type-parameters`, `interface-extraction-nested-signature-type-qualification`, `cross-project-interface-event-member-preservation`, `interface-extraction-public-accessor-contracts`. The validation rows (`cross-project-interface-reference-binding`, `cross-project-interface-signature-dependencies`, `interface-extraction-receiving-language-compatibility`, `interface-extraction-conflict-check-fail-closed`) and the using/import rows stay independent.
