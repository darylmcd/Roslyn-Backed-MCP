---
category: Maintenance
---

- **Maintenance:** Refactored `PromptShimTools`'s parameter binder: replaced the no-op `async` `BuildParameterValuesAsync` (CC 11) with a synchronous `BuildParameterValues` plus focused `ParseParametersDocument`, `EnsureRequiredParametersPresent`, `ResolveParameterValue`, and `DeserializeParameterValue` helpers, each ≤8 cyclomatic complexity. No behavior change to `get_prompt_text` — precedence, error wording, and `JsonDocument` disposal are preserved.
