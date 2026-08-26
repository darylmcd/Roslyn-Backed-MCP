---
category: Fixed
---

- **Fixed:** Stopped the scripting-service timeout tests pinning one side of a terminal-shape race — a non-cooperative script may end via the watchdog kill or the cooperative timeout, and either is correct — and gave the unbounded-output test enough wall clock for the IPC-limit path to win its race so its assertion stays strict rather than being loosened.
