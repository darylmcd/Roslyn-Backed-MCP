---
category: Fixed
---

- **Fixed:** Reverting an apply that was captured as a solution snapshot (no file snapshots) now fails closed, retaining the snapshot, when the apply also added or removed documents or projects or changed project/metadata/analyzer references or parse/compilation options, instead of restoring text only and reporting success. The refusal is scoped to the snapshot's own affected files, so a later file-disjoint apply does not block an earlier revert.
