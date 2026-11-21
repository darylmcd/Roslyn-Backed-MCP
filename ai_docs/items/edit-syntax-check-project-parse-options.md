# edit-syntax-check-project-parse-options — Validate edits in loaded C# parse contexts

**row:** `edit-syntax-check-project-parse-options` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs:745-783`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Derive the parser options from every affected loaded C# document, including linked files that differ by project. Red-first: a malformed branch controlled by a project-defined symbol must fail without mutating any file, while an inactive branch retains the intended current behavior. Preserve unrelated syntax and preview controls.

## Evidence

- EditService.cs:747 parses with default options on every syntax-check path. A pinned Roslyn 5.9.0 probe of a stray closing brace inside `#if PROJECT_DEFINE` produced no default diagnostics and CS1022 when the same parser enabled that symbol.

## Context

- Re-vet the stable apply_text_edit result and refusal contract before selection. The titled-region false positive has a distinct cause (preprocessing messages set ContainsSkippedText).

## Notes

5.x screen at a12412bb: the default-options parser is shared by stable apply_text_edit and both experimental multi-file routes. The complete project-context fix can change an existing stable successful apply into a refusal when a loaded project defines a symbol. Qualify linked documents and the stable wire behavior together. If the refusal change requires a migration, reopen at the next major with ADR and both raw protocol eras.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
