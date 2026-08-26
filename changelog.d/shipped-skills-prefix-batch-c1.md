---
category: Fixed
---

- **Fixed:** Shipped skills `semantic-find`, `test-coverage`, and `trace-flow` now resolve the Roslyn MCP tool prefix once at runtime and pin it, instead of instructing agents to call a hard-coded `mcp__roslyn__` literal — marketplace-plugin installs no longer hit a false connectivity halt. Their residual-unswept amnesty entries were removed from the genericity guard's allowlist, and the shrink ratchet tightened from 5 to 2.
