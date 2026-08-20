---
category: Fixed
---

- **Fixed:** `semantic_grep` invalid-pattern failures no longer carry raw .NET regex-parser text or the submitted pattern in the thrown message or debug log, and the public `InvalidArgument` envelope now returns stable, actionable regex-correction guidance instead of the generic parameter fallback. Error-message text is not a stability contract; the response shape, category, `paramName`, and `schemaHint` are unchanged.
