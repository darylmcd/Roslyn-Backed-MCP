---
category: Fixed
---

- **Fixed:** `compile_check` no longer reports `success: true, errorCount: 0` when a `files[]` filter resolves to zero workspace documents — the zero-resolution case now fails loud (`success: false`, `completedProjects: 0`, `totalProjects: 0`, `requestedScope == actualScope == "files"`, plus a hint naming the miss) instead of silently widening to a full-solution compile and filtering every diagnostic away by the nonexistent requested path. The legitimate multi-project widening path is unchanged. Closes `compile-check-zero-resolution-false-success`.
