---
category: Fixed
---

- **Fixed:** Preserve the complete containing type identity when splitting a nested type into a partial file. The experimental split_class_preview tool now returns an empty preview with guidance for selected file-local types and types with a file-local ancestor at any depth, whose identity cannot span physical files.
