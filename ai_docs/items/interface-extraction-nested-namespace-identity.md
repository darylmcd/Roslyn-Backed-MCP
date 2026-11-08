# interface-extraction-nested-namespace-identity — Preserve full namespace identity

**row:** `interface-extraction-nested-namespace-identity` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:198`
- (new) `tests/RoslynMcp.Tests/StandaloneInterfaceAliasRouteTests.cs`

## Acceptance

- [ ] Standalone extraction of a type inside nested block namespaces emits the full outer-to-inner namespace name, preserving escaped identifiers and generated interface symbol identity.
- [ ] The pending and applied solutions compile, and the generated interface has the original full namespace identity.

## Evidence

- At `eed3273083b4c72784893d25c8be41cadfe8c876`, `BuildInterfaceFile` reads only the nearest `BaseNamespaceDeclarationSyntax.Name`. A type in `Outer.Inner` is emitted into `Inner`, changing its public name. The standalone alias route fixture exposed the defect while testing nested namespace alias visibility.

## Context

- Fix alongside `interface-extraction-standalone-extern-alias` because both changes reconstruct the standalone generated compilation unit in `BuildInterfaceFile`. Keep the namespace check independent of alias syntax.
