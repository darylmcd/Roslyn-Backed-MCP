---
category: Fixed
---

- **Fixed:** `extract_type_preview` now refuses, before issuing a preview token, to move instance members out of a record, record struct or struct with an `ExtractTypeBlockingDependencyException` naming each instance member — the injected composition field entered record equality, `GetHashCode` and `with` copies and left `default(T)` of a struct with a null helper; static-only moves from these types still succeed with no injected field. Closes `extract-type-value-type-and-record-composition`.
