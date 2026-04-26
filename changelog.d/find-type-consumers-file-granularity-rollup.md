---
category: Added
---

- **Added:** `find_type_consumers(typeName, limit) → [{filePath, kinds, count}]` MCP tool — file-granularity rollup over the existing reference index, removes the Grep fallback for "which files touch this type" workflows. Site kinds classified as `using-directive` / `ctor` / `inherit` / `field-declaration` / `local-declaration` (with `other` for unrecognized contexts). Registered in the experimental tier; surface count bumped to **166 tools** (111 stable / 55 experimental). Closes `find-type-consumers-file-granularity-rollup`.
