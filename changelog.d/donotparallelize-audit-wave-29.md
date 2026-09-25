---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `SymbolRelationshipsBuiltinTypeSuppressionTests.cs` and `SymbolSearchPaginationTests.cs` — removed both after proving them safe with repeated concurrent runs (read-only queries against the synchronized shared-workspace cache), with a source-adjacent comment recording each decision. `TestAssemblyFixtureTests.cs` carried no opt-out, so there was nothing to audit. Closes `donotparallelize-audit-wave-29`.
