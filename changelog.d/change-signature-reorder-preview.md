---
category: Added
---

- **Added:** `change_signature_preview` now supports `op="reorder"` for method parameters. Reorders the declaration and rewrites every invocation argument list semantically — positional callers are reordered to match the new declaration order, named arguments are preserved verbatim, and ambiguous shapes (default values mid-reorder, `ref`/`out` migrations) surface structured refusals. Closes `change-signature-reorder-preview`.
