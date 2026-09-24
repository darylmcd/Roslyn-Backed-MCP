---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `ReplaceInvocationTests.cs`, `RestructureServiceTests.cs`, and `SamplingMrtrWireTests.cs` — removed all three after proving them safe with repeated concurrent runs (preview-only work against per-test isolated copies, the synchronized shared-workspace cache, or per-test in-memory MCP harnesses), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-23`.
