# extract-type-conditional-namespace-layout-ownership — Preserve alternate lexical namespace layouts in generated extraction files

**row:** `extract-type-conditional-namespace-layout-ownership` · **pri:** `Medium` · **size:** `M` · **verified:** `9e7a94395a5cbc09a79b0d4c6b8387ce65809d39`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1187`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionNamespace.cs` (new)
- `tests/RoslynMcp.Tests/ExtractionNamespaceIdentityTests.cs` (new)

## Acceptance

- [ ] Produce an extraction preview in each branch of a valid source whose preprocessor directives switch one file-scoped namespace to multiple nested block namespaces around a shared type body. Every extraction must bind the intended namespace when applied in its loaded configuration.
- [ ] Preserve the same applied edit when reparsed with the alternate configuration, including scope-specific externs/usings and ambient lookup. Verify the emitted declaration identity and exact retained caller binding, not only compilation.
- [ ] Qualify the complete fix against the stable 5.x extract_type_preview contract before selection. Preserve existing error envelopes and structured member names.

## Evidence

- Cold re-review found a header group that opens two lexical namespace scopes in one configuration and one in the other. Per-scope header copying sees the nested namespace before the shared closing directive, leaving no single header boundary to carry the group.
- The 9e7a9439 emitter follows only the innermost loaded namespace and cannot preserve this layout switch across builds. Loaded full namespace identity and same-scope conditional name/delimiter projection are handled separately.
