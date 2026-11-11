# extract-type-nested-namespace-identity — Preserve the full extracted type namespace

**row:** `extract-type-nested-namespace-identity` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384` · **deps:** `class-split-nested-namespace-identity,split-service-nested-namespace-identity`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1187-1196`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] A public extraction from a nested namespace emits the extracted type under the original full namespace. The serialized preview and applied solution compile, and generated references bind to that type.

## Evidence

- Cold namespace plan review at `34a87d9c`. The extracted file emitter copies only the innermost namespace name. Reuse the full containing namespace identity and preserve receiving-language syntax.
