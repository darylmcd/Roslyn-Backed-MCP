---
category: Fixed
---

- **Fixed:** `change_signature_preview` add/remove/reorder no longer refuses with "An existing argument has no original semantic parameter binding" when an invocation argument is a null-operation expression such as `x!` or `checked(...)` (this made every preview on `RoslynMcp.slnx`, including `PhysicalPathResolver.CanonicalizeDriveRoot`, fail), and `replace_invocation_preview` no longer misreads such an argument as a params-expanded element; argument-to-parameter lookup now goes through one shared `ArgumentOperationResolver`. Part of `change-signature-unrelated-invocation-rewrite`.
