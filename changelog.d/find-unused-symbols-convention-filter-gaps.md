---
category: Fixed
---

- **Fixed:** `find_unused_symbols(excludeConventionInvoked=true)` no longer false-positives holders decorated with `[McpServerToolType]` / `[McpServerPromptType]` / `[McpServerResourceType]`, xUnit `[CollectionDefinition]` classes, or ASP.NET health-check response writers (public methods with `(HttpContext, HealthReport)` signature bound to `HealthCheckOptions.ResponseWriter` by delegate). `UnusedCodeAnalyzer.IsConventionInvokedType` extended via name-shape detection — no new NuGet dependencies (`find-unused-symbols-convention-filter-gaps`).
