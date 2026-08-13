---
category: Fixed
---

- **Fixed:** `semantic_grep` keeps its method description below the 2,000-character client cap without dropping regex, token-scope, timeout, pagination, or result-limit guidance; a reflection regression now caps every MCP tool description. Closes `semantic-grep-description-2kb-overflow`.
