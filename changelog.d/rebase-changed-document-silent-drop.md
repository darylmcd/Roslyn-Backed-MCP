---
category: Fixed
---

- **Fixed:** Applying a preview whose reviewed edit, added file or added project reference targets a document or project that a sibling apply has since removed now returns `Success:false` with the existing invalid-preview error instead of `Success:true` with the edit silently dropped (`rebase-changed-document-silent-drop`).
