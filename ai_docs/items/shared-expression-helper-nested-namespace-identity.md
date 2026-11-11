# shared-expression-helper-nested-namespace-identity — Preserve the full shared-expression helper namespace

**row:** `shared-expression-helper-nested-namespace-identity` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384` · **deps:** `class-split-nested-namespace-identity,split-service-nested-namespace-identity`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:659-663`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] A cross-file extraction from a type under nested namespaces rewrites occurrences to the helper in its full namespace. Compile the preview and applied solution, and verify that a same-name type under only the innermost namespace cannot capture the rewritten call.

## Evidence

- Cold namespace plan review at `34a87d9c`. The cross-file helper qualification uses only the nearest namespace name. Reuse the full containing namespace identity with the appropriate syntax escaping.
