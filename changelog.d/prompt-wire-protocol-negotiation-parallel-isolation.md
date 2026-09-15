---
category: Fixed
---

- **Fixed:** Prevent delayed in-memory MCP discovery from silently downgrading to the legacy handshake. Preserve the overall initialization deadline and verify three concurrent modern/legacy rounds across the SDK probe threshold. Closes `prompt-wire-protocol-negotiation-parallel-isolation`.
