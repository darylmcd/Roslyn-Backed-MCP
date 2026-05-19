---
category: Fixed
---

- **Fixed:** `project_diagnostics` returning a severity-filtered `totalDiagnostics` count when a `severityFilter` is applied (e.g. `severity=Error` collapsed `totalDiagnostics` to the error count only). `totalDiagnostics` now matches `totalErrors + totalWarnings + totalInfo` and is invariant under severity filtering, consistent with the documented behavior of the other total fields. Closes gh #746.
