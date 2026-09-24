---
category: Fixed
---

- **Fixed:** `find_dead_fields`, `find_unused_symbols` and `get_coupling_metrics` no longer report live code as unreferenced in projects with source generators; the compilation cache now serves the Solution-owned compilation for symbol analysis and keeps the generator-rerun compilation for diagnostics only.
