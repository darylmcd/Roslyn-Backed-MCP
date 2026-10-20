---
category: Fixed
---

- **Fixed:** `change_signature_preview` and `format_range_preview` caller refusals (missing or unsupported `op`, add/remove/rename/reorder inputs, parenthesized `metadataName`, non-method target, and the eight range coordinate, order and bounds checks) now publish authored, path-free corrections naming the real parameter instead of the generic fallback, without echoing request text; categories and `exceptionType` are unchanged. Closes `change-signature-service-refusals-public-message` and `format-range-service-refusals-public-message`; `change-signature-omitted-optional-binding` was already fixed by #1779 (covered by `ChangeSignaturePreview_PreservesOriginalRuntimeBindings`).
