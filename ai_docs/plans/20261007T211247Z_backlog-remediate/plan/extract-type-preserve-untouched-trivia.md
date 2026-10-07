| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `TypeExtractionService.cs:142-144` normalizes the complete updated compilation unit, rewriting unrelated source trivia after extraction. The synthesized new file can be formatted independently. |
| Approach | - [ ] `extract_type_preview` formats only synthesized or changed syntax, leaving unchanged source regions byte-identical.<br>- [ ] A red-first regression includes unusual whitespace outside the extraction and compares the untouched bytes after preview/apply. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`. Tests 1: `tests/RoslynMcp.Tests/TypeExtractionTests.cs`. Own fragment; no deletions. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Preserve untouched source bytes, comments, directives and line endings; format only synthesized/changed nodes. Probe every NormalizeWhitespace call in this service and distinguish new-document generation from existing source rewriting. No signature or contract changes; no production ripple beyond the defining service. |
| Validation | Red-first copied-sample extraction with unusual whitespace outside the extraction; compare unchanged byte regions before/after preview and apply, and compile the result. Run TypeExtractionTests and related extraction classes discovered by test_related_files. Per-edit compile_check and targeted test_run; scoped executor gate; required hosted validate per CI_POLICY.md is the full landing gate, without duplicating hosted checks locally. |
| Performance review | N/A - correctness fix; inspect alias-resolution cost at index rebuild, not repeated enumeration. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Type extraction preserves source formatting outside the changed syntax. |
| Backlog sync | Close rows: [extract-type-preserve-untouched-trivia]. |
