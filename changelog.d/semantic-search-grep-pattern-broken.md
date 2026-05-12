## Fixed

- `semantic_search` description now explicitly states the tool does **not** use embedding-based
  or vector similarity search — matching is done via structured Roslyn predicate parsing (symbol
  kind, modifiers, return types, etc.) with a token-substring fallback for queries that do not
  parse to a structured predicate. Previously the name alone could mislead callers into expecting
  embedding semantics.
- `semantic_grep` description now documents that the pattern syntax is **.NET regex**
  (System.Text.RegularExpressions), not ripgrep/PCRE syntax, and that per-document evaluation is
  hard-capped at 2 seconds with silent skip-on-timeout rather than call failure. Callers can use
  simpler patterns if results appear incomplete on large files.

Closes gh #627.
