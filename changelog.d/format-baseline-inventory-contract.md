---
category: Maintenance
---

- **Maintenance:** Added a deterministic repository formatter baseline inventory (`eng/generate-format-baseline.ps1` plus the tracked `eng/format-baseline.json`) and a `FormatterBaselineContractTests` contract test asserting the inventory is sorted, deduplicated, internally consistent, and free of suppressed or relabeled diagnostics. Inventory only — no formatting violations were repaired or silenced.
