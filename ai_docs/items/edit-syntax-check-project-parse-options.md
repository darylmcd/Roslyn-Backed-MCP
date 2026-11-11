# edit-syntax-check-project-parse-options — Validate edits in loaded C# parse contexts

**row:** `edit-syntax-check-project-parse-options` · **pri:** `Medium` · **size:** `S` · **verified:** `52f2c418cf5ba982b2a1c0914dd3977655d93eb4`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs:745-783`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Derive the parser options from every affected loaded C# document, including linked files that differ by project. Red-first: a malformed branch controlled by a project-defined symbol must fail without mutating any file, while an inactive branch retains the intended current behavior. Preserve unrelated syntax and preview controls.

## Evidence

- EditService.cs:747 parses with default options on every syntax-check path. A pinned Roslyn 5.9.0 probe of a stray closing brace inside `#if PROJECT_DEFINE` produced no default diagnostics and CS1022 when the same parser enabled that symbol.

## Context

- Re-vet the stable apply_text_edit result and refusal contract before selection. The titled-region false positive has a distinct cause (preprocessing messages set ContainsSkippedText).
