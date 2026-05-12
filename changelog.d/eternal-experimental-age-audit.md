---
category: Added
---

- **Added:** `eng/audit-experimental-age.ps1` — advisory script that walks `ServerSurfaceCatalog.*` for experimental-tier entries, runs `git blame` on each catalog line, computes age, emits a table of entries older than 180 days. Does not auto-promote or auto-deprecate. Optional advisory hook added to `/publish-preflight` Step 8.5.
