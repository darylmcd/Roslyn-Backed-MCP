---
category: Fixed
---

- **Fixed:** `workspace_load` and other path-taking tools now return an actionable error naming `ROSLYNMCP_SANCTIONED_ROOTS` / `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` when no sanctioned roots are configured, instead of a generic invalid-parameter message. The requested path stays redacted from the client message.
