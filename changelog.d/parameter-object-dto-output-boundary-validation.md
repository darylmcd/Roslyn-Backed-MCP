---
category: Fixed
---

- **Fixed:** `parameter_object_preview` now validates `dtoNamespace` and `dtoFolders` before combining them with the project directory — refusing rooted and traversal segments that could place the generated DTO outside the target project — and refuses destination collisions (existing document, existing file on disk, or existing type of the same name) before a preview token is stored, so no token is minted and no directory is created or file overwritten on a refused request.
