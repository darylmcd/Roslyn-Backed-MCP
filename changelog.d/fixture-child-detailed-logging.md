---
category: Fixed
---

- **Fixed:** Fixture `dotnet restore`/`build`/`msbuild` children in the workspace restore and fork-admission tests now run with `-v:detailed`, and their failure messages keep the last 64 KB of stdout and stderr, so the ubuntu-26.04 silent exit-1 failures print the failing task's own output. Test-only diagnostic scaffolding; remove with the root-cause row.
