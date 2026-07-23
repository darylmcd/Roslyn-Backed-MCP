---
category: Maintenance
---

- **Maintenance:** decomposed `ToolErrorHandler.ClassifyError` (CC 11) and `TryClassifyBindingLike` (CC 9) into focused, precedence-preserving stages — `TryClassifyReloadRace`, `TryClassifyRegisteredHandler`, `BuildUnexpectedErrorFallback`, and a dictionary-dispatch rewrite of the binding-like classifier — each measuring CC ≤ 8. Binding, reload-race, registered-handler dictionary order, and fallback precedence are unchanged; all named parameter-validation, reload-race, not-found, observability, and stale-token regression suites pass unmodified.
