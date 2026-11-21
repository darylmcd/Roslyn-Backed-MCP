# symbol-refactor-edit-default-parse-options — SymbolRefactorService parses the edited text with default options

**row:** `symbol-refactor-edit-default-parse-options` · **pri:** `Low` · **size:** `S` · **verified:** `79ad03bf9291670cb708a6501b1b6297a29f89cf` · **deps:** `edit-syntax-check-project-parse-options`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/SymbolRefactorPreviewTests.cs`

## Acceptance

- [ ] The parse at SymbolRefactorService.cs:~607 uses the document project parse options, red-first test with a symbol- or LangVersion-dependent snippet.

## Evidence

- Plan of edit-syntax-check-project-parse-options (section 9 sketch).
