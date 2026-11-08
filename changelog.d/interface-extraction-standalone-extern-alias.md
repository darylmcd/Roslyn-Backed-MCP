---
category: Fixed
---

- **Fixed:** Standalone interface extraction now carries the required visible `extern alias` and alias-qualified imports into generated interface files, including aliases declared inside namespaces. Generated interfaces also keep the full namespace identity of types declared under nested namespaces. Closes `interface-extraction-standalone-extern-alias`.
