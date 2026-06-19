---
category: Maintenance
---

- **Maintenance:** The `WorkspaceValidationService` kill-failure observability test now drives the Warning-log path through the injected `killProcessTree` seam via a compile-checked `internal` call, instead of reflecting into the private `TryKillProcessTree` — so a rename of the helper produces a compile error rather than a runtime `Invoke` failure. Closes `workspace-validation-kill-test-reflection-seam`.
