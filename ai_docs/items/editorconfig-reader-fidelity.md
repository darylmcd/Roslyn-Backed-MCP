# editorconfig-reader-fidelity — Make the editorconfig reader agree with the Roslyn parser

**row:** `editorconfig-reader-fidelity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:85,99` (double parse)
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:164` (empty values dropped)
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:337` (nonexistent key)
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:450-460` (ReadConfigLines splits CR/LF only)
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] get_editorconfig_options reports an option cleared with an empty value as the parser does; ReadConfigLines uses the parser line model so upsert key matching agrees on files containing NEL/LS/PS; the invalid key is removed; one parse per read.
- [ ] Regression tests fail on old code; the sibling policy-held rows editorconfig-section-source-path-matching and editorconfig-writer-effective-section-precedence are not widened.

## Evidence

- Observed while implementing editorconfig-option-assignment-grammar-validation. Static findings except the Roslyn parser behavior measured in that review.
