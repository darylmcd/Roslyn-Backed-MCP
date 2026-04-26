---
category: Added
---

- **Added:** Cross-server tool aliases (`get_symbol_outline` → `document_symbols`, `find_duplicated_code` → `find_duplicated_methods`, `get_test_coverage_map` → `test_coverage`) so agents migrating from python-refactor (Jedi) land on the canonical tool. Aliases include a `deprecation.canonicalName` field on the response envelope; canonical tools include the same field with `null` value to keep the schema uniform. Closes `roslyn-mcp-sister-tool-name-aliases`.
