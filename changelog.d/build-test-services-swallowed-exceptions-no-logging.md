---
category: Fixed
---

- **Fixed:** `ScaffoldingService`, `EditorConfigService`, and `MsBuildEvaluationService` now accept an optional `ILogger<T>` and log previously-silent fallback paths (malformed project-file parses defaulting to mstest / allowing non-test projects through; disk-sourced `.editorconfig` overrides; project-not-found resolution) instead of swallowing the underlying exception with no diagnostic trail.
- **Fixed:** Wired SuppressionService to structured logging and made unreadable-file pragma and line-ending fallbacks observable without changing edit or cancellation behavior.
