---
category: Maintenance
---

- **Maintenance:** Documented the deliberate exception swallow on the workspace-fork restore timeout path. `RestoreForkAsync`'s best-effort `try { process.Kill(entireProcessTree: true); } catch { }` (killing a timed-out `dotnet restore` fork) now carries a boundary comment explaining the swallow is intentional — a Kill failure is non-actionable because a `TimeoutException` is already being thrown — matching the deliberate-boundary convention in `WorkspaceTools.cs`. Closes `swallow-fork-kill-empty-catch`.
