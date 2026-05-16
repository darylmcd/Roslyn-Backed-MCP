---
category: Fixed
---

- **Fixed:** `audit-phase-runner` subagents dispatched for Phases 3 and 4 of `/mcp-server-surface-test --full` now use a deterministic type/method selection rule (top-N by cyclomatic score; alphabetical fallback) and are explicitly prohibited from calling `AskUserQuestion`.
