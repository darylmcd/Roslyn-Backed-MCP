---
category: Fixed
---

- **Fixed:** `extract_type_preview` now assigns the composition field before the first original statement of every root constructor (block and converted expression-bodied), so constructor code that reaches an extracted member through a retained helper no longer dereferences a null field. Part of `extract-type-composition-assigned-before-use`; the base-constructor refusal remains tracked in `extract-type-composition-base-ctor-refusal`.
