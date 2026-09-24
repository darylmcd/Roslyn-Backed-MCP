---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `SymbolInfoNotFoundMessageTests.cs`, `StringLiteralReplaceServiceTests.cs`, and `SuppressionToolsTests.cs` — removed all three after proving them safe with repeated concurrent runs (read-only lookups and preview-only calls through the synchronized shared-workspace cache, or per-test isolated copies with per-test in-memory MCP hosts), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-27`.
