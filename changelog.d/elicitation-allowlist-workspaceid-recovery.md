---
category: Fixed
---

- **Fixed:** Missing `workspaceId` calls on eligible read-only workspace tools can now elicit a workspace path, load it, and retry once with the recovered workspace id. Closes `elicitation-allowlist-workspaceid-recovery`.
