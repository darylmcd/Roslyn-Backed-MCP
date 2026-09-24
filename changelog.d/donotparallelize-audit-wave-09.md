---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `FetchMcpResourceReadinessTests.cs`, `FindImplementationsCorlibHintTests.cs`, and `FindOverloadsTests.cs` — removed the opt-outs on `FindImplementationsCorlibHintTests` and `FindOverloadsTests` after proving them safe with repeated concurrent runs, and documented the retained opt-out on `FetchMcpResourceReadinessTests` (it reloads the assembly-shared sample workspace). Closes `donotparallelize-audit-wave-09`.
