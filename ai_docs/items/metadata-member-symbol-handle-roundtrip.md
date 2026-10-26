# metadata-member-symbol-handle-roundtrip — Resolve returned metadata member handles

**row:** `metadata-member-symbol-handle-roundtrip` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:27` (`CreateHandle`, `ResolveHandleAsync`, payload validation)
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs` (locator resolution)
- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:34` (public reference consumer)
- `tests/RoslynMcp.Tests/DocumentSymbolsSymbolHandleTests.cs`
- `tests/RoslynMcp.Tests/FindReferencesSummaryTests.cs`

## Acceptance

- [ ] A metadata overload candidate returned by public discovery can be selected by its exact returned symbolHandle and resolved through public find_references; reproduce the NameSyntax FileScopedNamespaceDeclaration failure before repair.
- [ ] Encode and resolve stable exact identities for metadata-only members, including overloads, generic containing types, constructors, properties, fields and events. Never resolve a different overload or publish a handle that has no resolution path.
- [ ] Preserve source and named-type handle behavior, malformed-payload diagnostics, cancellation and workspace ownership; cover source/metadata and overload ambiguity controls without broad unsupported-member fences.

## Evidence

- Live reviewer request metadataName=Microsoft.CodeAnalysis.CSharp.SyntaxFactory.FileScopedNamespaceDeclaration returned ambiguous=true/count3 with the NameSyntax overload selectable by symbolHandle; feeding that exact handle back returned NotFound / KeyNotFoundException.
- Decoded returned payload: MetadataName=null, DisplayName=Microsoft.CodeAnalysis.CSharp.SyntaxFactory.FileScopedNamespaceDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax), FilePath=null, Line=null, Column=null.
- Current primary semantic source verifies CreateHandle records MetadataName only for INamedTypeSymbol; ResolveHandleAsync tries metadata/source paths and returns null without resolving a display-only payload that ParseHandlePayload accepts.
- Healthy source-position navigation remains available after filing; no claim of repair or authorization to disable metadata navigation. Observed during namespace-copy review on 2026-10-26; this independent tooling fix is not selected for this batch.
