---
category: Changed
---

- **Changed:** The legacy flat location members of `SymbolDto`, `DiagnosticDto` and `TypeUsageDto` (`FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`, plus `ContainingMember`, `PreviewText` and `Classification` on `TypeUsageDto`) are now `[Obsolete]`; they stay serialized and populated and nothing is removed, and the removal is scheduled for the next major release (ADR 0001, `docs/product-contract.md` § Deprecations scheduled for 5.0). Migration: read the nested `Location` and fall back to the flat members only when it is null, which happens for partial spans such as a build diagnostic with a start but no end. Closes `locationdto-stage3-flat-field-deprecation`.
