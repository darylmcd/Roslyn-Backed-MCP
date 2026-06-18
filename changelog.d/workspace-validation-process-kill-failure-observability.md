---
category: Fixed
---

- **Fixed:** Swallowed `Process.Kill` cleanup failures in `WorkspaceValidationService`: kill exceptions are now logged at Warning level (with process id) when a logger is present, matching `DotnetCommandRunner`. Validation envelopes unchanged.
