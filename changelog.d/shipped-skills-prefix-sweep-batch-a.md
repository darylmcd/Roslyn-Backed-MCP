---
category: Fixed
---

- **Fixed:** Shipped `exception-audit`, `format-sweep`, `generate-tests`, and `impact-assessment` skills no longer halt for marketplace-plugin installs. Their connectivity precheck now uses the canonical resolve-once-then-pin flow (scan for any tool whose name ends in `server_info`, identify the Roslyn one by response shape, pin that prefix) instead of a hard-coded `mcp__roslyn__` literal.
