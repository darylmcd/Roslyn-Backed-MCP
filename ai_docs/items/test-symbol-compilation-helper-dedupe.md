# test-symbol-compilation-helper-dedupe — Share identical symbol compilation fixture setup

**row:** `test-symbol-compilation-helper-dedupe` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SymbolTraversalTests.cs:216`
- `tests/RoslynMcp.Tests/TypeNamespaceWalkerTests.cs:86`
- `tests/RoslynMcp.Tests/Helpers/SymbolCompilationFixture.cs (new)`

## Acceptance

- [ ] Extract the identical CompileAsync implementations to one helper; keep source-specific compilations isolated.
- [ ] Make synchronous compilation construction explicit instead of Task.FromResult plus unnecessary awaits; update callers.
- [ ] Preserve reference/options identity and all traversal/walker assertions; deduplicate immutable references only after confirming aliases and properties match.

## Evidence

- Token-normalized complete method bodies are identical, including four MetadataReference.CreateFromFile calls; wrapper performs no asynchronous work.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
