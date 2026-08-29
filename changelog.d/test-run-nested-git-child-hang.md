---
category: Fixed
---

- **Fixed:** non-interactive command and PowerShell test runners now close dedicated stdin pipes so nested native children cannot inherit the MCP protocol input stream; build-server test cleanup is bounded and observable instead of swallowing failures. Closes `test-run-nested-git-child-hang`.
