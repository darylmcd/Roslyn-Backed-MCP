---
category: Fixed
---

- **Fixed:** `change_signature_preview(op=add)` now defaults to end-append when the caller supplies a valid `name`/`parameterType` without an index, instead of failing with a misleading "Parameter 'index' has an out-of-range value" internal-index error (`ChangeSignatureService`). (`change-signature-preview-add-unhelpful-error`)
