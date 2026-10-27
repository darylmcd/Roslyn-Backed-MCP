# synthesized-syntax-platform-newline — Synthesized syntax ignores the file's line ending

**row:** `synthesized-syntax-platform-newline` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:123`, `:819`, `:861` — `Formatter.FormatAsync(..., Formatter.Annotation, options: null)`: elastic newlines in synthesized nodes take the platform default.
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:380`, `:383`, `:384`, `:397`, `:447`, `:982` — hardcoded `SyntaxFactory.ElasticCarriageReturnLineFeed`; the call-statement (`:447`) and closing-brace (`:384`) breaks sit at the formatted-span boundary and survive formatting as literal CRLF.
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:325` — same `options: null` scoped format.
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:768-769` — hardcoded `ElasticCarriageReturnLineFeed` on synthesized facade trivia; `:802`, `:1880` — `Environment.NewLine` / `NormalizeWhitespace` new-file text.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:604`, `:625` — new-file root uses `NormalizeWhitespace()` (CRLF) plus `Environment.NewLine` (platform), so a Linux-built new file can mix endings and never follows the source file or `end_of_line`.
- Shared helper to reuse: `src/RoslynMcp.Roslyn/Helpers/AnnotatedFormatting.cs` (pins `FormattingOptions.NewLine` to the original text's ending).

## Acceptance

- [ ] Every synthesized line break in extract_method, extract_shared_expression_to_helper, namespace relocation and split_service_with_di facade output uses the edited file's own line ending (or `end_of_line` when set), on Windows and Linux.
- [ ] Route every annotation-scoped `Formatter.FormatAsync` through `AnnotatedFormatting.FormatAsync`; replace hardcoded `ElasticCarriageReturnLineFeed` with the file's break (or the replaced statement's own break where one exists).
- [ ] New files created by extract_type / split_service pick one consistent ending (source file's, else `end_of_line`) instead of mixing `NormalizeWhitespace` CRLF with `Environment.NewLine`.
- [ ] Red-first LF and CRLF regressions per producer; each fails on current main on at least one platform.

## Evidence

- 2026-10-27, Windows: an extract_method LF/CRLF `[DataRow]` regression (`ExtractMethod_SynthesizedLines_UseTheFileLineEnding`, appended to `tests/RoslynMcp.Tests/ExtractMethodFormatRegressionTests.cs`) failed for the LF file on main: the call statement line ended `\r\n`. Routing through `AnnotatedFormatting` and reusing the replaced statement's break still left `}\r\n` after the synthesized method's closing brace (`:384`), so the hardcoded trivia must be replaced at every site, not only the formatter options.
- Split out of `extract-type-preserve-untouched-trivia` (PR claude/extract-type-trivia), which fixed the source-file path for extract_type only.

## Context

- Distinct from `new-file-apply-writes-bom-ignoring-editorconfig` (byte-order mark), but the same new-file write paths.
