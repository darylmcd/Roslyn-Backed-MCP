---
category: Fixed
---

- **Fixed:** Auto-reload cascade no longer crashes the stdio host. `WorkspaceManager.LoadIntoSessionAsync` now builds the replacement `MSBuildWorkspace` into locals and atomically swaps before disposing the prior workspace, closing the window where concurrent readers hit a disposed workspace. `GetCurrentSolution` wraps the null-workspace and residual `ObjectDisposedException` paths into a structured `StaleWorkspaceTransitionException` — `ToolErrorHandler` maps it to a retry-able `category="StaleWorkspaceTransition"` envelope instead of letting an `InternalError` escape and kill the process (`autoreload-cascade-stdio-host-crash`).
