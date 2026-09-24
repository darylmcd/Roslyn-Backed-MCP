---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `FindReferencesSummaryTests.cs`, `FindReflectionUsagesPaginationTests.cs`, and `FixAllServiceGuidanceTests.cs` — removed all three opt-outs after proving them safe with repeated concurrent runs (readers of the synchronized shared workspace or per-test isolated fixture copies, no apply/reload/process-global state), each documented with a source-adjacent rationale comment. Closes `donotparallelize-audit-wave-11`.
