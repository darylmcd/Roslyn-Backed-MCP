# 07 — C6 Capability parity and repo-side

Rows (check C6):

- `netanalyzers-package-duplicates-sdk-analyzers` (Medium) — NetAnalyzers package + SDK built-in analyzers both run (every CA diagnostic reported twice)
- `logging-capability-parity` (Low) — initialize advertises logging capability but server_info says logging:false
- `root-sample-solution-tree-duplicate` (Low) — Stale duplicate sample tree at repo root

The generator-rerun blindness (C1 rows `compilation-cache-generator-rerun-blinds-unused-analysis` and `find-type-consumers-mutations-generator-blind`) is also a parity failure: the tools promise solution-wide reference analysis, and the implementation silently returns empty for generator projects.
