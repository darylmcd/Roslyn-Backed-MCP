# unused-code-analyzer-convention-complexity — Isolate convention filtering

**row:** `unused-code-analyzer-convention-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:308`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:342`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:857`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:880`
- `tests/RoslynMcp.Tests/UnusedSymbolsTestBridgeExclusionTests.cs`

## Acceptance

- [ ] Extract convention/framework-glue classification behind one focused internal collaborator.
- [ ] Reduce each listed method below CC10 while preserving attribute, namespace, delegate, and framework-invoked exclusions.
- [ ] Table-test the extracted decision boundary with positive and negative cases.

## Evidence

- Current-session touched-file complexity review measured CC11–17 across four convention-classification methods in the 1,392-line analyzer.
