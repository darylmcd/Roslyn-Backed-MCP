---
category: Fixed
---

- **Fixed:** `extract_interface_preview` emitting a duplicate interface file when the target type already implements a covering interface. Previously the preview diff included a new `IFoo.cs` even when the type's base list already contained `IFoo`; the apply would create a conflicting file. The base list is also correctly left unmodified in this case. Fixes gh #748.
