---
category: Fixed
---

- **Fixed:** Experimental multi-file edit and preview batches reject multiple groups that resolve to the same file, including alternate path spellings and directory links. Combine the edits for that file into one group. The refusal occurs before any preview token, undo capture, or file write.
