---
category: Maintenance
---

- **Maintenance:** Converted the two compile-time-constant DI-registration regexes in `SymbolRefactorService` (the `split_service_with_di_preview` rewrite path) from per-call `new Regex(...)` to `[GeneratedRegex]` source-generated partials, resolving SYSLIB1045. Match behavior is unchanged (identical patterns and `RegexOptions`). Closes `symbolrefactor-static-regex-generatedregex`.
