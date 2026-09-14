---
category: Fixed
---

- **Fixed:** Cohesion analysis resolves partial-type members against their own syntax trees and emits one metric per logical type, restoring refactoring suggestions for multi-file partial classes. Clustering preserves overloaded method and helper identities, including constructed generic calls, while retaining the existing DTO shape and simple-name labels. Closes `cohesion-multifile-partial-semantic-model` and `cohesion-overload-cluster-symbol-identity`.
