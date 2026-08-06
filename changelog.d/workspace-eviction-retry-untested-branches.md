---
category: Maintenance
---

- **Maintenance:** Added direct test coverage for the workspace-eviction auto-retry's non-recovering branches — a bogus/never-loaded workspace id (no reload attempted), a failed rehydration reload (original failure preserved unchanged), and a mid-call `WorkspaceEvictedException` (evicted strictly between the gate precheck and the deeper service lookup, hand-rolled envelope asserted byte-for-byte) — previously only the reload-succeeds and no-`IWorkspaceManager`-wired paths were exercised (PR #1141 follow-up).
