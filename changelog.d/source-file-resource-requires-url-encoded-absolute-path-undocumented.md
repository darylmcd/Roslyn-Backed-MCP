---
category: Fixed
---

- **Fixed:** `source_file_lines` resource description incorrectly stating filePath "must be URL-encoded" — both URL-encoded and raw absolute paths have always been accepted by the shared normalizer. Description now matches `source_file`'s accurate phrasing. Closes gh #762.
