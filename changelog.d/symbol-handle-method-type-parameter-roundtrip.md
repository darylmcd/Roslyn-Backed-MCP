---
category: Fixed
---

- **Fixed:** Newly minted symbol handles for supported constructed types preserve the exact generic method and type parameter ordinal. Unsupported method owners return the existing NotFound result for newly minted handles. Existing handles keep their previous behavior. Earlier servers return NotFound for the new method-owned handles. Request a fresh handle from an upgraded server to recover the construction.
