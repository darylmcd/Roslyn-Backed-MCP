---
category: Fixed
---

- **Fixed:** `find_implementations` now canonicalizes results on `ISymbol` identity and emits a single user-authored source location per symbol by default, suppressing source-generator-emitted partials (detected via null-`Document` lookup plus `.g.cs` / `.generated.cs` suffix). A new `includeGeneratedPartials` opt-in on `ReferenceService.FindImplementationsAsync` and the `find_implementations` MCP tool restores the raw per-declaration list (`find-implementations-source-gen-partial-dedup`).
