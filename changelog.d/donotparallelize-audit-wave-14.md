---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `HighValueCoverageIntegrationTests.cs` and `IntegrationTests.SymbolNavigation.cs` — removed both after proving them safe with repeated concurrent runs (read-only use of the synchronized shared workspace cache), and made `CompileCheck_BuildFailureSolution_Reports_Errors` close its private workspace session instead of leaking a slot. `InMemoryMcpClientServerHarness.cs` carries no attribute (only a historical doc note) and is unchanged. Closes `donotparallelize-audit-wave-14`.
