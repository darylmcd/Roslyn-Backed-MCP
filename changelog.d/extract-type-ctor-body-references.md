---
category: Fixed
---

- **Fixed:** `extract_type_preview` no longer refuses an extraction because retained constructor bodies (block, expression-bodied, or chained `this(...)`) call an extracted instance member; the composition field is assigned before the body runs, so only references inside a `this(...)`/`base(...)` initializer argument are still refused. Closes type-extraction-ctor-body-refusal-overbroad.
