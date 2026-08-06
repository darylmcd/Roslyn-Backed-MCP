---
category: Fixed
---

- **Fixed:** `compile_check`'s `projectFilter` handling is now normalized once at `CheckAsync` entry so a whitespace-only `projectName` is treated as "no filter" consistently across `ProjectFilterHelper.FilterProjects`, scope classification (`requestedScope`/`actualScope`), and the zero-projects hint — previously it was silently matched as a literal (nonexistent) project name, producing `actualScope:"solution"` alongside `TotalProjects:0`.
