---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `PromptShimToolsTests.cs`, `PromptSmokeTests.cs`, and `RecordFieldAdditionImpactTests.cs` — removed all three opt-outs after proving them safe with repeated concurrent runs; each class only reads through the synchronized shared workspace cache, renders read-only prompts or stubs, or loads and closes its own isolated fixture copy. Closes `donotparallelize-audit-wave-21`.
