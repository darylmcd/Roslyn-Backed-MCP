---
category: Fixed
---

- **Fixed:** Linux workspace-close tests copy their launcher in a separate process, preventing a concurrent fork in the test host from retaining the executable's write handle and causing `Text file busy` at launch.
