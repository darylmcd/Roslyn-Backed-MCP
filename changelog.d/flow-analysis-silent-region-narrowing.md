---
category: Fixed
---
- **Fixed:** `analyze_data_flow` / `analyze_control_flow` now return `effectiveStartLine`/`effectiveEndLine` and a warning when a range spanning multiple blocks or members is narrowed to one block, instead of silently analyzing a different region. (`flow-analysis-silent-region-narrowing`)
