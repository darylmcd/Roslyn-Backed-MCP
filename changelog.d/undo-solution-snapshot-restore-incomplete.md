---
category: Fixed
---

- **Fixed:** Reverting an apply that was captured as a solution snapshot (no file snapshots) now fails closed, retaining the snapshot, when the apply also added or removed documents or projects or changed project/metadata/analyzer references or parse/compilation options, instead of restoring text only and reporting success. Projects and documents are matched by path, so a reload's new ids do not trigger a refusal. A snapshot that recorded affected files only refuses for changes involving those files, so a later file-disjoint apply does not block an earlier revert; a snapshot with no recorded files (for example an options-only change) is checked against the whole solution diff.
