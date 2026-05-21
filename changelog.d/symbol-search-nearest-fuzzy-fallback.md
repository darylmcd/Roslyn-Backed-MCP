---
category: Fixed
---

- **Fixed:** Metadata-name NotFound responses can now include structured `closestMatches` suggestions so near-miss `symbol_info` and shared resolver paths point agents at likely symbols instead of forcing a separate search round trip. Closes `symbol-search-nearest-fuzzy-fallback`.
