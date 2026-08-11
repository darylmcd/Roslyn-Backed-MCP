---
category: Fixed
---

- **Fixed:** `fix_all` (`scope: "project"`) now throws an `ArgumentException` naming `projectName` when it is missing or whitespace, instead of silently resolving `ProjectFilterHelper`'s blank-means-no-filter project list down to `solution.Projects.First()` and previewing fixes against the wrong project (`fixall-blank-projectname-silent-wrong-target`).
