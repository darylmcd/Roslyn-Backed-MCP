# preview-diff-whitespace-omission — Preserve whitespace mutations in preview diffs

**row:** `preview-diff-whitespace-omission` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs:38` two-argument BuildDiffModel ignores whitespace differences.
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs:50` empty diff drops a changed document from preview changes.
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:508` formatting uses the shared diff producer.
- `tests/RoslynMcp.Tests/DiffGeneratorTests.cs` shared diff regression coverage.

## Acceptance

- Compare source text without ignoring whitespace; expose every whitespace mutation represented by the stored solution. Re-derive all shared diff producers and consumers.
- Add regressions for trailing spaces, indentation, whitespace-only lines and mixed edits; observe old behavior fail. Retain identical-input no-op, bounded hunks and truncation semantics.
- Verify formatting and explicit edit previews expose changed files and their actual hunks before apply; unchanged previews remain empty. Include preview/apply parity coverage and assess public contract documentation.

## Evidence

- Live DiffPlex 1.9.0 DLL probe: BuildDiffModel("class C { }   \n", "class C { }\n") yields only Unchanged pieces; overload with ignoreWhitespace=false yields Deleted/Inserted pieces. DiffGenerator.cs calls the former and returns empty when no hunks were written.
- SnipCue bl-0522 concrete text-edit preview returned 13 changed file entries with empty unifiedDiff while whitespace edits were applied and dotnet format then passed. Retained artifact: C:/Users/daryl/AppData/Local/Temp/snipcue-remediate-20261004/20261004T122130Z_backlog-remediate/whitespace-preview-0522-evidence.json.
- Formatting preview returned changes=[]; whether Roslyn Formatter also omitted a particular trailing-space mutation must be re-derived separately. This row proves the shared diff omission, not formatter parity.
