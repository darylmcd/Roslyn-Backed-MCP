---
category: Fixed
---

- **Fixed:** Interface extraction preserves nullable annotation contexts on emitted signatures across standalone, cross-project, dependency-inversion and extract-and-wire previews without changing warning policy; previews refuse newly introduced compiler errors with bounded diagnostic guidance when the receiving compiler policy cannot represent the nullable contract.
