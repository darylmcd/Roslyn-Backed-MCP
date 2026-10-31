---
category: Fixed
---

- **Fixed:** Analyzer shadow cleanup now holds no-follow directory handles while sweeping stale lease roots, so a replaced shared or workspace directory cannot redirect cleanup into external data. Closes `analyzer-shadow-sweep-path-authority`.
