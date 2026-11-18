# symbol-search-namespace-filter-display-string — SymbolSearchService namespace filter compares display strings, not namespace identity

**row:** `symbol-search-namespace-filter-display-string` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:192`

## Acceptance

- [ ] A namespaceFilter naming a keyword-segment namespace in either spelling matches its symbols; test with `Foo.class` / `Foo.@class`.
- [ ] Uses the shared NamespaceNameIdentity helper once relocation-using-semantic-identity lands (same mechanism).

## Evidence

- reloc-b review (2026-11-18): same display-string-versus-caller-spelling mechanism fixed at the relocation sites by NamespaceNameIdentity; this site was not covered.
