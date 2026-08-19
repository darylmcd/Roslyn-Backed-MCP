---
category: Fixed
---

- **Fixed:** `extract_type_preview` leaving the injected composition field uninitialized on most construction paths. The extracted type is now wired through every instance constructor — implicit (a constructor is synthesized), overloaded, `this(...)`-chained (the delegating call forwards the new argument), and expression-bodied (rewritten to a block body with the assignment) — and unsupported topologies (primary constructors/records, bodyless constructors) are refused before a preview is generated instead of producing a diff whose applied result is silently broken.
