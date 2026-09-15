---
category: Fixed
---

- **Fixed:** Reference pages now honor an operator-configurable 32,000-byte JSON ceiling and provide additive `nextOffset` continuation without skipping references; clients should follow `nextOffset` while `hasMore=true`. Oversized individual references fail with recovery guidance. Workflow recommendations name registered tools, and bootstrap guidance explains default automatic workspace reloads. Closes `list-tool-response-byte-budget` and `server-guidance-callable-tools-autoreload`.
