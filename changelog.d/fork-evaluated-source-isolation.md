---
category: Fixed
---

- **Fixed:** Experimental workspace forks reject evaluated imports, inputs and restore outputs that reach the source workspace. Native load and build checks also guard package preprocessing, assets-cache writes and copied outputs, including late project references and physical aliases. Retained forks preserve these checks when reopened or tested.
