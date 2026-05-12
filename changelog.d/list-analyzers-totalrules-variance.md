---
category: Fixed
---

- **Fixed:** `list_analyzers` returning non-deterministic `totalRules` counts across sessions against the same workspace. The service-layer deduplication guard was exiting early on the first project to reference an analyzer assembly, discarding rules visible only from other projects' language contexts. The fix accumulates rules from all projects before deduplicating by rule ID, making the result session-stable.
