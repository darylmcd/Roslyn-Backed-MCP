---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `set_editorconfig_option` now refuses a key or value that is not exactly one provider-valid single-line assignment (line breaks, `=`/`:`/space/bracket key syntax, inline `#`/`;` value comments) as a named `InvalidArgument` refusal before any write, and `set_diagnostic_severity` and the pragma tools refuse a `diagnosticId` containing whitespace, control characters or separators. `add_pragma_suppression` no longer accepts a comma-separated id list (one diagnostic id per call; call it once per id). A leading or trailing line break is refused, not trimmed. Valid arbitrary options, empty values and `unset` are unchanged. **Migration:** send single-line keys and values that the Roslyn analyzer-config parser reads back unchanged; match refusals by category and parameter. See [ADR 0028](../docs/decisions/0028-editorconfig-assignment-grammar-refusals.md). Closes `editorconfig-option-assignment-grammar-validation`.
