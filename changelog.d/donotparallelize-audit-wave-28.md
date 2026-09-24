---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `SymbolDisambiguationElicitationTests.cs`, `SymbolImpactSweepBudgetTests.cs`, and `SymbolMapperTests.cs` — removed all three after proving them safe with repeated concurrent runs (read-only access to the shared sample workspace through the synchronized cache, class-local services, per-test fakes and in-memory `AdhocWorkspace` fixtures), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-28`.
