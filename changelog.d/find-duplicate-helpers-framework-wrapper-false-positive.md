---
category: Fixed
---

- **Fixed:** `find_duplicate_helpers` false positives for thin `System.*` / `Microsoft.*` single-forwarder helpers (minimal API, `HttpClient` helpers, etc.): new `DuplicateHelperAnalysisOptions.ExcludeFrameworkWrappers` (default `true`), optional `excludeFrameworkWrappers` on the tool, and matching logic in `UnusedCodeAnalyzer`. Closes `find-duplicate-helpers-framework-wrapper-false-positive` (PR #350).
