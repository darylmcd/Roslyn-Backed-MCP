---
category: Changed — BREAKING
---
- **Changed — BREAKING:** Explicit empty validation scope now stays empty; omit changedFilePaths for tracker/Git fallback. Symbol search rejects offset + limit beyond Int32.MaxValue. Test-run refusals publish safe corrections without caller identifiers, project-list remedies request workspace_status with verbose=true, and filtered runs reporting zero tests carry a warning without changing execution counts/status. Migration for the next major release: use representable page endpoints, select a test project or add a test SDK, and inspect warnings/counts before accepting filtered verification. See ADR 0027.
