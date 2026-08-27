# code-pattern-analyzer-responsibility-split — Separate reflection scanning from semantic query parsing

**row:** `code-pattern-analyzer-responsibility-split` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodePatternAnalyzer.cs`
- `src/RoslynMcp.Roslyn/Services/ReflectionUsageAnalyzer.cs`
- `src/RoslynMcp.Core/Services/ICodePatternAnalyzer.cs`
- `tests/RoslynMcp.Tests/FindReflectionUsagesPaginationTests.cs`
- `tests/RoslynMcp.Tests/CodePatternAnalyzerTests.cs`

## Acceptance

- [ ] Extract reflection-usage traversal and completeness accounting into one focused collaborator while preserving the existing public interface and tool response contracts.
- [ ] Leave semantic-query parsing and search behavior independently testable; do not duplicate syntax-tree traversal, failure projection, or cancellation policy.
- [ ] Regression tests pin reflection ordering/completeness and semantic-query parsing/search before and after the split.
- [ ] The original service becomes a thin compatibility facade or is renamed only under the public compatibility policy.

## Evidence

- `CodePatternAnalyzer` is 637 lines and currently owns reflection traversal, query parsing, syntax search, semantic filtering, ordering, and result projection.
- Adding scan-completeness policy increased coupling between unrelated reflection and semantic-query paths, making isolated maintenance and review harder.
