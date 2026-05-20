---
category: Fixed
---

- **Fixed:** `get_source_text` reporting `totalLineCount` one higher than the `source_file_lines` resource marker for files ending with a newline; both surfaces now route through `SourceTextSlicer.CountLines` so the counts are consistent. Fixes gh #769 §13.26.
