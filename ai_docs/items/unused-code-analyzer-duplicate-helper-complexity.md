# unused-code-analyzer-duplicate-helper-complexity — Decompose duplicate-helper analysis

**row:** `unused-code-analyzer-duplicate-helper-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:648`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:753`
- `tests/RoslynMcp.Tests/DuplicateHelperDetectionTests.cs`

## Acceptance

- [ ] Separate candidate enumeration, semantic equivalence classification, and result projection.
- [ ] Reduce `FindDuplicateHelpersAsync` and `TryClassifyHelperAsDuplicate` below CC10.
- [ ] Preserve overload, conversion, generic, and delegation distinctions.

## Evidence

- Current-session touched-file complexity review measured CC13 in both duplicate-helper methods.
