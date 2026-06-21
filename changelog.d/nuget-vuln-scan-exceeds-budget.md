---
category: Fixed
---

- **Fixed:** `nuget_vulnerability_scan` now emits a `"scanning-nuget"` progress stage during the network-bound `dotnet list package --vulnerable` call so MCP clients can distinguish an active scan from a hang. Adds a cache-hit regression test confirming a warm repeat scan (same workspace version + lock-file hash) skips the CLI invocation entirely. (The result cache and configurable timeout shipped in a prior pass.)
