---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `symbol_info` and the nested `document_symbols` member-walk now return `kind="Record"` (or `"RecordStruct"`) for positional record types, where `symbol_info` previously returned `kind="Class"` for record classes. Callers switching on `"Class"` for records must update. Fixes gh #769 §13.20.
