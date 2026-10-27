---
category: Fixed
---

- **Fixed:** `extract_type_preview` / `extract_type_apply` no longer reformat the whole source file. Only the synthesized composition field, constructor, constructor parameter, forwarded `this(...)` argument and assignment are formatted, in the file's own line ending; every other byte (including unusual whitespace, comments and LF or CRLF line endings) is left exactly as written. Closes `extract-type-preserve-untouched-trivia`.
