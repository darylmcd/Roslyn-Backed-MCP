---
category: Fixed
---

- **Fixed:** `analyze_dependencies` prompt payload overflow: namespace-dependency nodes and edges are now capped at 50 each (down from 100) and circular-dependency cycles are capped at 20, preventing the 63 KB inline-payload overflow observed on 9-project workspaces. Closes gh #755.
